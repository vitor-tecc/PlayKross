#include "../src/Module.cpp"
#include "../src/ObsAudioSource.cpp"
#include <iostream>

static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
struct Lifetime final : jk::IProcessLifetime {
    jk::ProcessToken token;
    explicit Lifetime(jk::ProcessToken value = {42, 1000, L"C:\\controller.exe"}) : token(std::move(value)) {}
    jk::ProcessToken Identity() const override { return token; }
    bool Alive() const override { return true; }
};
struct Probe final : jk::IProcessProbe {
    jk::ProcessToken token{42, 1000, L"C:\\controller.exe"};
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return std::make_shared<Lifetime>(token); }
};
struct Fixture {
    Probe probe;
    obs_source_t source{"Application audio"};
    jk::AudioSource context{&source, 1};
    std::unique_ptr<obs_source_t> replacement;
    std::unique_ptr<jk::AudioSource> replacement_context;
    jk::AudioRoute original{"jk_process_audio", "Application audio", 1, false, jk::AudioMode::Application, "process:42:1000:C:\\controller.exe"};
    Fixture() {
        auto bound = jk::ProcessBinding::Open(Lifetime{}.Identity(), probe);
        context.binding = std::move(bound.binding);
        context.identity_key = original.identity_key;
        context.captured_frames.store(800);
        context.emitted_frames.store(400);
        jk::sources.emplace(&source, &context);
        harness_sources.push_back(&source);
        auto owner = jk::ProcessBinding::Open(Lifetime{}.Identity(), probe);
        Require(jk::module_guard.Arm(std::move(owner.binding), original, 1), "Fixture must arm original audio selection");
    }
    ~Fixture() { jk::module_guard.Disarm(); jk::sources.clear(); harness_sources.clear(); }
    void SubstituteReadySameNameSource() {
        // Remove A and register a separately bound, ready B under the same name.
        replacement = std::make_unique<obs_source_t>();
        replacement->name = source.name;
        replacement_context = std::make_unique<jk::AudioSource>(replacement.get(), 1);
        probe.token = {99, 2000, L"C:\\Other.exe"};
        auto bound = jk::ProcessBinding::Open(probe.token, probe);
        replacement_context->binding = std::move(bound.binding);
        replacement_context->identity_key = "process:99:2000:C:\\Other.exe";
        replacement_context->captured_frames.store(9000);
        replacement_context->emitted_frames.store(7000);
        replacement_context->startup_error = "replacement-only diagnostic";
        jk::sources.erase(&source);
        jk::sources.emplace(replacement.get(), replacement_context.get());
        harness_sources = {replacement.get()};
        Require(jk::AudioSourceReady(replacement->name.c_str(), 1, 0), "Replacement is independently ready");
        Require(!jk::SourceAllowed(replacement.get(), 1), "Replacement PCM must be denied by immutable identity");
        Require(replacement->mixers == 0, "Replacement starts already muted");
    }
};
static void SameNameReplacementFailsSelectedAudit() {
    Fixture f; f.SubstituteReadySameNameSource();
    Require(!jk::AuditScene(), "Selected identity loss must fail audit even when the ready replacement mixer is zero");
    jk::last_heartbeat_ns.store(os_gettime_ns());
    jk::Tick(nullptr, 0);
    Require(!jk::module_guard.Active() && jk::module_guard.StopReason() == jk::GuardStopReason::AudioGraphUnsafe,
        "Selected identity loss must stop with audio_graph_unsafe");
}
static void SameNameReplacementDiagnosticsUnavailable() {
    Fixture f; f.SubstituteReadySameNameSource();
    obs_data_t response; jk::DescribeSelectedAudio(&response);
    Require(response.strings["audioIdentity"] == f.original.identity_key, "Diagnostics retain the original selected identity");
    Require(!response.bools["audioRunning"], "Replacement must not supply selected audio running status");
    Require(response.ints["capturedAudioFrames"] == 0 && response.ints["emittedAudioFrames"] == 0, "Replacement counters must not be reported for the selected identity");
    Require(!response.strings["audioError"].empty() && response.strings["audioError"] != "replacement-only diagnostic", "Missing selected context must report unavailable");
}
static void OriginalContextAppliesMixersThenAuditsAndDescribes() {
    Fixture f;
    Require(!jk::AuditScene() && f.source.mixers == 1, "Initial audit applies the original selected mixer");
    Require(jk::AuditScene(), "Second original-context audit passes after mixer application");
    obs_data_t response; jk::DescribeSelectedAudio(&response);
    Require(response.bools["audioRunning"] && response.ints["capturedAudioFrames"] == 800 && response.ints["emittedAudioFrames"] == 400,
        "Original selected context supplies its running status and counters");
}
static void SystemContextUsesSelectedEndpointAndRejectsChanges() {
    for (const std::string change : {"endpoint", "mode", "generation"}) {
        Probe probe; obs_source_t source{"System audio", "jk_system_audio"};
        jk::AudioSource context{&source, 7, jk::AudioMode::SystemOutput};
        auto bound = jk::ProcessBinding::Open(probe.token, probe);
        context.binding = std::move(bound.binding); context.endpoint_id = L"render:selected";
        context.identity_key = "endpoint:render:selected";
        jk::sources.emplace(&source, &context); harness_sources = {&source};
        auto owner = jk::ProcessBinding::Open(probe.token, probe);
        jk::AudioRoute route{"jk_system_audio", "System audio", 7, false, jk::AudioMode::SystemOutput, "endpoint:render:selected"};
        Require(jk::module_guard.Arm(std::move(owner.binding), route, 7), "System route arms");
        Require(!jk::AuditScene() && source.mixers == 1 && jk::AuditScene(), "Owned system context must participate in selected audit");
        Require(!jk::AudioSourceReady("System audio", 7, 99), "System context must reject a different controller owner");
        Require(jk::SourceAllowed(&source, 7) && !jk::SourceAllowed(&source, 6), "Only current system generation may emit");
        obs_data_t response; jk::DescribeSelectedAudio(&response);
        Require(response.strings["audioMode"] == "SystemOutput" && response.strings["audioIdentity"] == "endpoint:render:selected" && response.bools["audioRunning"], "System diagnostics must retain selected endpoint identity");
        obs_data_t settings; settings.ints["selection_generation"]=7; settings.strings["endpoint_id"] = change == "endpoint" ? "render:other" : "render:selected";
        if (change == "mode") settings.strings["audio_mode"] = "Application";
        if (change == "generation") settings.ints["selection_generation"] = 8;
        jk::Update(&context, &settings);
        Require(!jk::SourceAllowed(&source, 7) && !context.binding->CanEmit(), "Changing endpoint, mode, or generation must stop and seal original context");
        jk::module_guard.Disarm(); jk::sources.clear(); harness_sources.clear();
    }
}
int main(int argc, char** argv) {
    try {
        const std::string name = argc == 2 ? argv[1] : "";
        if (name == "SameNameReplacementFailsSelectedAudit") SameNameReplacementFailsSelectedAudit();
        else if (name == "SameNameReplacementDiagnosticsUnavailable") SameNameReplacementDiagnosticsUnavailable();
        else if (name == "OriginalContextAppliesMixersThenAuditsAndDescribes") OriginalContextAppliesMixersThenAuditsAndDescribes();
        else if (name == "SystemContextUsesSelectedEndpointAndRejectsChanges") SystemContextUsesSelectedEndpointAndRejectsChanges();
        else throw std::runtime_error("Unknown case");
        std::cout << "PASS " << name << '\n'; return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
