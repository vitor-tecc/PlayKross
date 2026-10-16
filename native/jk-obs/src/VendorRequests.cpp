#include "ModuleState.hpp"
#include <obs-websocket-api.h>
#include <util/platform.h>
#include <windows.h>

namespace jk {
namespace {
obs_websocket_vendor vendor = nullptr;
std::mutex vendor_mutex;
std::wstring Wide(const char* text) {
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, nullptr, 0);
    if (size <= 1) return {};
    std::wstring result(static_cast<std::size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, result.data(), size);
    result.pop_back(); return result;
}
void Status(obs_data_t*, obs_data_t* response, void*) {
    std::lock_guard lock(vendor_mutex);
    obs_data_set_bool(response, "armed", module_guard.Active());
    obs_data_set_string(response, "stopReason", GuardStopReasonCode(module_guard.StopReason()));
    obs_data_set_bool(response, "graphSafe", AuditScene());
    DescribeSelectedAudio(response);
    DescribeVideo(response);
}
void Arm(obs_data_t* request, obs_data_t* response, void*) {
    std::lock_guard lock(vendor_mutex);
    try {
        module_guard.Disarm();
        const auto pid = obs_data_get_int(request, "ownerPid");
        if (pid <= 0 || pid > UINT32_MAX) throw std::runtime_error("Invalid controller process.");
        ProcessToken owner{static_cast<std::uint32_t>(pid), std::stoull(obs_data_get_string(request, "ownerCreationFileTime")), Wide(obs_data_get_string(request, "ownerImagePath"))};
        const auto controller = ControllerIdentity();
        if (!controller || controller->pid != owner.pid || controller->creation_filetime != owner.creation_filetime || controller->image_path != owner.image_path) throw std::runtime_error("Capture controller does not match the private IPC owner.");
        auto binding = ProcessBinding::Open(owner);
        if (!binding.binding) throw std::runtime_error(binding.error);
        const auto generation = static_cast<std::uint64_t>(obs_data_get_int(request, "generation"));
        const std::string name = obs_data_get_string(request, "audioSource");
        const std::string video_name = obs_data_get_string(request, "videoSource");
        if (!VideoSourceReady(video_name.c_str(), generation)) throw std::runtime_error("Original capture item is unavailable.");
        // During managed migration only, absence of audioMode infers Off/Application.
        // Explicit callers must use the canonical enum strings; never guess a fallback.
        const std::string mode = obs_data_has_user_value(request, "audioMode") ? obs_data_get_string(request, "audioMode") : name.empty() ? "Off" : "Application";
        if (mode != "Off" && mode != "Application" && mode != "SystemOutput") throw std::runtime_error("Unknown audio mode. Use Off, Application or SystemOutput.");
        if ((mode == "Off") != name.empty()) throw std::runtime_error("Audio mode and selected source do not match.");
        std::optional<AudioRoute> selected;
        if (mode != "Off") {
            if (!AudioSourceReady(name.c_str(), generation, owner.pid)) throw std::runtime_error("Selected audio is unavailable or belongs to room playback. " + AudioSourceError(name.c_str(), generation));
            selected = SelectedAudioRoute(name.c_str(), generation, owner.pid);
            if (!selected || selected->mode != (mode == "SystemOutput" ? AudioMode::SystemOutput : AudioMode::Application)) throw std::runtime_error("Selected audio mode or identity is unavailable.");
            if (obs_data_has_user_value(request, "audioIdentity") && selected->identity_key != obs_data_get_string(request, "audioIdentity")) throw std::runtime_error("Selected audio identity does not match the prepared source.");
        }
        else if (obs_data_has_user_value(request, "audioIdentity") && *obs_data_get_string(request, "audioIdentity")) throw std::runtime_error("Audio off cannot include an identity.");
        if (!module_guard.Arm(std::move(binding.binding), selected, generation)) throw std::runtime_error("Capture guard could not be armed.");
        SelectGuardVideo(video_name, generation);
        last_heartbeat_ns.store(os_gettime_ns());
        AuditScene(); // First apply the selected mixer (new sources start muted).
        if (!AuditScene()) throw std::runtime_error("Selected capture audio graph is unavailable or unsafe.");
        obs_data_set_bool(response, "armed", true);
    } catch (const std::exception& error) {
        module_guard.Disarm(); AuditScene();
        obs_data_set_bool(response, "armed", false); obs_data_set_string(response, "error", error.what());
    }
}
void Disarm(obs_data_t*, obs_data_t* response, void*) { StopPublication(); obs_data_set_bool(response, "armed", false); }
void Heartbeat(obs_data_t*, obs_data_t* response, void*) { last_heartbeat_ns.store(os_gettime_ns()); obs_data_set_bool(response, "armed", module_guard.Active()); obs_data_set_string(response, "stopReason", GuardStopReasonCode(module_guard.StopReason())); }
}
void RegisterVendorRequests() {
    vendor = obs_websocket_register_vendor("jkiller");
    if (!vendor) { blog(LOG_ERROR, "[JKiller] private vendor control unavailable; publishing remains disarmed"); return; }
    obs_websocket_vendor_register_request(vendor, "Arm", Arm, nullptr);
    obs_websocket_vendor_register_request(vendor, "Disarm", Disarm, nullptr);
    obs_websocket_vendor_register_request(vendor, "Heartbeat", Heartbeat, nullptr);
    obs_websocket_vendor_register_request(vendor, "GetSafetyStatus", Status, nullptr);
    RegisterPipelineRequests(vendor);
}
void UnregisterVendorRequests() {
    if (!vendor) return;
    UnregisterPipelineRequests(vendor);
    for (const char* name : {"Arm", "Disarm", "Heartbeat", "GetSafetyStatus"}) obs_websocket_vendor_unregister_request(vendor, name);
}
}
