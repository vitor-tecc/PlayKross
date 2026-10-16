#include "ModuleState.hpp"
#include "InputPolicy.hpp"
#include <windows.h>
#include <map>
#include <mutex>
#include <tlhelp32.h>

namespace jk {
namespace {
std::wstring Wide(const char* text) {
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, nullptr, 0);
    if (size <= 1) return {};
    std::wstring result(static_cast<std::size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, result.data(), size);
    result.pop_back(); return result;
}
std::string ProcessKey(const ProcessToken& identity) {
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, identity.image_path.data(), static_cast<int>(identity.image_path.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) throw std::runtime_error("Invalid native application image identity.");
    std::string image(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, identity.image_path.data(), static_cast<int>(identity.image_path.size()), image.data(), size, nullptr, nullptr);
    return "process:" + std::to_string(identity.pid) + ":" + std::to_string(identity.creation_filetime) + ":" + image;
}
bool BelongsToOwnerTree(const ProcessToken& target, std::uint32_t owner_pid) {
    if (!owner_pid) return false;
    const auto owner = ReadProcessToken(owner_pid);
    if (!owner) return true; // Unverifiable controller: fail closed.
    if (target.creation_filetime < owner->creation_filetime) return false;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return true;
    std::unique_ptr<void, decltype(&CloseHandle)> retained(snapshot, &CloseHandle);
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    std::map<std::uint32_t, std::uint32_t> parents;
    if (!Process32FirstW(snapshot, &entry)) return true;
    do { parents.emplace(entry.th32ProcessID, entry.th32ParentProcessID); } while (Process32NextW(snapshot, &entry));
    auto current = target.pid;
    for (int depth = 0; depth < 128; ++depth) {
        if (current == owner_pid) return true;
        if (!current) return false;
        const auto ancestor = ReadProcessToken(current);
        if (!ancestor) return true;
        if (ancestor->creation_filetime < owner->creation_filetime) return false;
        auto parent = parents.find(current);
        if (parent == parents.end() || parent->second == current) return true;
        current = parent->second;
    }
    return true;
}
struct AudioSource final : AudioSink {
    obs_source_t* source;
    std::unique_ptr<ProcessBinding> binding;
    ProcessLoopback capture;
    const std::uint64_t generation;
    std::string identity_key;
    std::atomic<std::uint64_t> captured_frames{0}, emitted_frames{0};
    std::string startup_error;
    explicit AudioSource(obs_source_t* value, std::uint64_t version) : source(value), generation(version) { }
    void OnPcm(const PcmBlock& block) override {
        const auto frames = static_cast<std::uint32_t>(block.samples.size() / block.channels);
        captured_frames.fetch_add(frames);
        if (!binding || !binding->CanEmit() || !SourceAllowed(source, generation)) return;
        obs_source_audio audio{};
        audio.data[0] = reinterpret_cast<const std::uint8_t*>(block.samples.data());
        audio.frames = frames; audio.speakers = SPEAKERS_STEREO; audio.format = AUDIO_FORMAT_FLOAT;
        audio.samples_per_sec = block.sample_rate; audio.timestamp = block.monotonic_ns;
        obs_source_output_audio(source, &audio);
        emitted_frames.fetch_add(frames);
    }
    void Stop() { if (binding) binding->Seal(); capture.Stop(); }
};
std::optional<AudioRoute> Route(obs_source_t* source, const AudioSource& context, std::uint64_t generation, std::uint32_t owner_pid) {
    if (!context.binding || !context.capture.Running() || !context.binding->CanEmit() || context.generation != generation || context.identity_key.empty() ||
        BelongsToOwnerTree(context.binding->Identity(), owner_pid) || obs_source_get_monitoring_type(source) != OBS_MONITORING_TYPE_NONE) return std::nullopt;
    return AudioRoute{"jk_process_audio", obs_source_get_name(source), context.generation, false, AudioMode::Application, context.identity_key};
}
std::mutex sources_mutex;
std::map<obs_source_t*, AudioSource*> sources;
const char* Name(void*) { return "PlayKross application audio (application and children)"; }
void* Create(obs_data_t* settings, obs_source_t* source) {
    auto context = std::make_unique<AudioSource>(source, static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation")));
    try {
        // The independent native policy must also hold when bypassing the UI.
        if (!AllowsCreateInput("jk_process_audio"))
            throw std::runtime_error("Application audio input is disabled.");
        const long long pid = obs_data_get_int(settings, "pid");
        if (pid <= 0 || pid > UINT32_MAX || static_cast<std::uint32_t>(pid) == GetCurrentProcessId()) throw std::runtime_error("Select an eligible application; OBS itself cannot be captured.");
        const std::string creation = obs_data_get_string(settings, "creation_filetime");
        std::size_t used = 0;
        const auto created = std::stoull(creation, &used);
        if (used != creation.size()) throw std::runtime_error("Invalid application creation identity.");
        ProcessToken token{static_cast<std::uint32_t>(pid), created, Wide(obs_data_get_string(settings, "image_path"))};
        if (!AllowsApplicationAudioImage(token.image_path)) throw std::runtime_error("Discord, browser hosts and room playback cannot be audio sources.");
        const auto controller = ControllerIdentity();
        if (!controller || BelongsToOwnerTree(token, controller->pid)) throw std::runtime_error("Room playback and the controller's process tree cannot be captured.");
        auto result = ProcessBinding::Open(token);
        if (!result.binding) throw std::runtime_error(result.error);
        context->binding = std::move(result.binding);
        context->identity_key = ProcessKey(context->binding->Identity());
        const auto started = context->capture.Start(*context->binding, *context);
        if (!started.started) context->startup_error = started.error;
    } catch (const std::exception& error) { context->startup_error = error.what(); }
    { std::lock_guard lock(sources_mutex); sources.emplace(source, context.get()); }
    return context.release();
}
void Destroy(void* data) {
    auto* context = static_cast<AudioSource*>(data);
    { std::lock_guard lock(sources_mutex); sources.erase(context->source); }
    context->Stop(); delete context;
}
void Update(void* data, obs_data_t* settings) {
    auto* context = static_cast<AudioSource*>(data);
    if (!context->binding) return;
    const auto& original = context->binding->Identity();
    if (obs_data_get_int(settings, "pid") != original.pid || Wide(obs_data_get_string(settings, "image_path")) != original.image_path || std::to_string(original.creation_filetime) != obs_data_get_string(settings, "creation_filetime") || static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation")) != context->generation)
        context->Stop(); // A new selection requires destroying and explicitly recreating the source.
}
}
void RegisterAudioSource() {
    obs_source_info info{}; info.id = "jk_process_audio"; info.type = OBS_SOURCE_TYPE_INPUT; info.output_flags = OBS_SOURCE_AUDIO;
    info.get_name = Name; info.create = Create; info.destroy = Destroy; info.update = Update;
    obs_register_source(&info);
}
void StopAllAudioSources() {
    std::vector<obs_source_t*> retained;
    { std::lock_guard lock(sources_mutex); for (const auto& [source, context] : sources) { (void)context; if (auto* ref = obs_source_get_ref(source)) retained.push_back(ref); } }
    for (auto* source : retained) {
        AudioSource* context = nullptr;
        { std::lock_guard lock(sources_mutex); auto found = sources.find(source); if (found != sources.end()) context = found->second; }
        if (context) context->Stop();
        obs_source_release(source);
    }
}
void DescribeAudioSource(const char* name, obs_data_t* response) {
    std::lock_guard lock(sources_mutex);
    for (const auto& [source, context] : sources) if (std::string(obs_source_get_name(source)) == name) {
        obs_data_set_bool(response, "audioRunning", context->capture.Running());
        obs_data_set_int(response, "capturedAudioFrames", static_cast<long long>(context->captured_frames.load()));
        obs_data_set_int(response, "emittedAudioFrames", static_cast<long long>(context->emitted_frames.load()));
        const auto error = context->startup_error.empty() ? context->capture.Error() : context->startup_error;
        obs_data_set_string(response, "audioError", error.c_str());
        return;
    }
    obs_data_set_bool(response, "audioRunning", false);
    obs_data_set_string(response, "audioError", "Application audio source is not available.");
}
std::optional<AudioRoute> SelectedAudioRoute(const char* name, std::uint64_t generation, std::uint32_t owner_pid) {
    if (!name || !*name) return std::nullopt;
    // Return a value snapshot. Never acquire the guard lock while holding this
    // registry lock, or stop/join the callback thread under either lock.
    std::lock_guard lock(sources_mutex);
    for (const auto& [source, context] : sources) if (std::string(obs_source_get_name(source)) == name)
        return Route(source, *context, generation, owner_pid);
    return std::nullopt;
}
bool SourceAllowed(obs_source_t* source, std::uint64_t generation) {
    if (!source) return false;
    std::optional<AudioRoute> route;
    {
        std::lock_guard lock(sources_mutex);
        const auto found = sources.find(source);
        if (found != sources.end()) route = Route(source, *found->second, generation, 0);
    }
    // The callback must authorize its own context, never a same-name replacement.
    return route && route->type_id == obs_source_get_unversioned_id(source) && module_guard.Allows(*route);
}
bool AudioSourceReady(const char* name, std::uint64_t generation, std::uint32_t owner_pid) {
    if (!name || !*name) return true; // Explicit video-only mode.
    return SelectedAudioRoute(name, generation, owner_pid).has_value();
}
void DescribeSelectedAudio(obs_data_t* response) {
    const auto selected = module_guard.SelectedAudio();
    obs_data_set_string(response, "audioMode", !selected ? "Off" : selected->mode == AudioMode::Application ? "Application" : "SystemOutput");
    obs_data_set_string(response, "audioSource", selected ? selected->source_name.c_str() : "");
    obs_data_set_string(response, "audioIdentity", selected ? selected->identity_key.c_str() : "");
    obs_data_set_bool(response, "audioRunning", false);
    obs_data_set_int(response, "capturedAudioFrames", 0);
    obs_data_set_int(response, "emittedAudioFrames", 0);
    obs_data_set_string(response, "audioError", "");
    if (selected) DescribeAudioSource(selected->source_name.c_str(), response);
}
}
