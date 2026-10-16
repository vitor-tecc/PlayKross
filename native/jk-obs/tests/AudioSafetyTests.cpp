#include "ProcessLoopback.hpp"
#include "SceneGuard.hpp"
#include <iostream>
#include <map>
#include <functional>
#include <stdexcept>

static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
struct Lifetime final : jk::IProcessLifetime {
    bool alive = true;
    jk::ProcessToken Identity() const override { return {42, 1000, L"C:\\controller.exe"}; }
    bool Alive() const override { return alive; }
};
struct Probe final : jk::IProcessProbe {
    std::shared_ptr<Lifetime> lifetime = std::make_shared<Lifetime>();
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return lifetime; }
};
struct Sink final : jk::AudioSink { int emitted = 0; void OnPcm(const jk::PcmBlock&) override { ++emitted; } };
static jk::PcmBlock Block(std::uint64_t time) { return {std::vector<float>(960, 0.25f), 2, 48000, time}; }
static jk::AudioRoute Application(std::uint64_t generation = 1) { return {"jk_process_audio", "Application audio", generation, false, jk::AudioMode::Application, "process:99:2000:C:\\Game.exe"}; }
static jk::AudioRoute System(std::uint64_t generation = 1) { return {"jk_system_audio", "Output audio", generation, false, jk::AudioMode::SystemOutput, "endpoint:{fixed-render-id}"}; }
static void Arm(jk::SceneGuard& guard, Probe& probe, std::optional<jk::AudioRoute> route, std::uint64_t generation = 1) {
    auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
    Require(guard.Arm(std::move(binding.binding), route, generation), "Valid controller and route must arm");
}
static void OverflowDropsOldestPcmWithin200ms() {
    jk::PcmQueue queue;
    for (int i = 0; i < 22; ++i) queue.Push(Block(i * 10000000ULL));
    Require(queue.DurationMs() <= 200.001, "Audio queue must not grow beyond 200ms");
    Require(queue.DroppedFrames() == 960, "Overload must report discarded frames");
    Require(queue.Pop()->monotonic_ns == 20000000ULL, "Drop oldest, keep newest audio");
}
static void NoBufferAfterSeal() {
    Probe probe;
    auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
    binding.binding->Seal();
    Sink sink;
    Require(!jk::DeliverAudio(*binding.binding, sink, Block(0)), "Sealed source must reject PCM");
    Require(sink.emitted == 0, "No audio may reach output after seal");
}
static void OnlyArmedModeIdentityCanEmit() {
    Probe probe;
    jk::SceneGuard guard;
    for (auto selected : {Application(), System()}) {
        Arm(guard, probe, selected);
        Require(guard.Allows(selected), "Selected owned route must emit");
        auto changed = selected; changed.identity_key += ":replacement";
        Require(!guard.Allows(changed), "Same name with replacement identity must not emit");
        changed = selected; changed.source_name = "Other source";
        Require(!guard.Allows(changed), "Other source name must not emit");
        changed = selected; changed.mode = selected.mode == jk::AudioMode::Application ? jk::AudioMode::SystemOutput : jk::AudioMode::Application;
        Require(!guard.Allows(changed), "Different mode must not emit");
        for (const char* kind : {"wasapi_input_capture", "wasapi_output_capture", "wasapi_process_output_capture"}) {
            changed = selected; changed.type_id = kind;
            Require(!guard.Allows(changed), "Generic device sources must never emit");
        }
        Require(!guard.Allows(selected.mode == jk::AudioMode::Application ? System() : Application()), "Unselected owned mode must not emit");
    }
}
static void OffAllowsNoAudioRoute() {
    Probe probe; jk::SceneGuard guard;
    Arm(guard, probe, std::nullopt);
    Require(guard.Active(), "Off retains a live video guard");
    Require(!guard.Allows(Application()) && !guard.Allows(System()), "Off must reject every audio mode");
    Require(!guard.SelectedAudio(), "Off has no selected context");
    Arm(guard, probe, Application()); guard.Disarm();
    Require(!guard.SelectedAudio() && !guard.Allows(Application()), "Disarm must clear selection");
}
static void StaleGenerationAndMonitoringDenied() {
    Probe probe; jk::SceneGuard guard;
    for (auto route : {Application(), System()}) {
        Arm(guard, probe, route);
        auto stale = route; stale.generation = 2;
        Require(!guard.Allows(stale), "Stale generation must not emit");
        auto monitored = route; monitored.monitoring = true;
        Require(!guard.Allows(monitored), "Monitoring must not emit");
        auto fresh = route; fresh.generation = 2; fresh.identity_key += ":fresh";
        Arm(guard, probe, fresh, 2);
        Require(!guard.Allows(route) && guard.Allows(fresh), "Rearm must reject the previous source");
    }
}
static void ControllerDeathDeniesBothModes() {
    for (auto route : {Application(), System()}) {
        Probe probe; jk::SceneGuard guard; Arm(guard, probe, route);
        probe.lifetime->alive = false;
        Require(!guard.Active() && !guard.Allows(route), "Controller death must prevent both modes from emitting");
    }
}
static void InvalidSelectionsCannotArm() {
    Probe probe; jk::SceneGuard guard;
    for (int invalid = 0; invalid < 6; ++invalid) {
        auto route = Application();
        switch (invalid) {
        case 0: route.mode = jk::AudioMode::Off; break;
        case 1: route.type_id = "wasapi_output_capture"; break;
        case 2: route.identity_key.clear(); break;
        case 3: route.source_name.clear(); break;
        case 4: route.generation = 2; break;
        case 5: route.monitoring = true; break;
        }
        auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
        Require(!guard.Arm(std::move(binding.binding), route, 1), "Invalid route must fail to arm");
        Require(!guard.Active() && !guard.SelectedAudio(), "Failed arm must leave no stale context");
    }
}
static void FirstStopReasonSurvivesCleanup() {
    Probe probe; jk::SceneGuard guard; Arm(guard, probe, std::nullopt);
    guard.Disarm(jk::GuardStopReason::HeartbeatExpired);
    guard.Disarm(); guard.Disarm(jk::GuardStopReason::VideoSourceLost);
    Require(guard.StopReason() == jk::GuardStopReason::HeartbeatExpired, "Cleanup must retain the first cause");
}
static void SuccessfulRearmClearsOldStopReason() {
    Probe probe; jk::SceneGuard guard;
    guard.Disarm(jk::GuardStopReason::VideoSourceLost);
    Require(!guard.Arm(nullptr, std::nullopt, 1), "Invalid controller must not arm");
    Require(guard.StopReason() == jk::GuardStopReason::VideoSourceLost, "Failed rearm retains the old failure");
    Arm(guard, probe, std::nullopt, 2);
    Require(guard.StopReason() == jk::GuardStopReason::None, "New capture clears a stale failure");
}
int main(int argc, char** argv) {
    std::map<std::string, std::function<void()>> cases{
        {"OverflowDropsOldestPcmWithin200ms", OverflowDropsOldestPcmWithin200ms}, {"NoBufferAfterSeal", NoBufferAfterSeal},
        {"OnlyArmedModeIdentityCanEmit", OnlyArmedModeIdentityCanEmit}, {"OffAllowsNoAudioRoute", OffAllowsNoAudioRoute},
        {"StaleGenerationAndMonitoringDenied", StaleGenerationAndMonitoringDenied}, {"ControllerDeathDeniesBothModes", ControllerDeathDeniesBothModes},
        {"InvalidSelectionsCannotArm", InvalidSelectionsCannotArm}, {"FirstStopReasonSurvivesCleanup", FirstStopReasonSurvivesCleanup},
        {"SuccessfulRearmClearsOldStopReason", SuccessfulRearmClearsOldStopReason}};
    try { cases.at(argc == 2 ? argv[1] : "")(); std::cout << "PASS " << argv[1] << '\n'; return 0; }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
