#include "ModuleState.hpp"
#include <obs-websocket-api.h>
#include <util/platform.h>
#include <windows.h>

namespace jk {
namespace {
obs_websocket_vendor vendor = nullptr;
std::string audio_source_name;
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
    obs_data_set_bool(response, "graphSafe", AuditScene());
    obs_data_set_string(response, "audioSource", audio_source_name.c_str());
    DescribeAudioSource(audio_source_name.c_str(), response);
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
        if (!AudioSourceReady(name.c_str(), generation, owner.pid)) throw std::runtime_error("Selected application audio is unavailable or belongs to room playback.");
        if (!module_guard.Arm(std::move(binding.binding), name, generation)) throw std::runtime_error("Capture guard could not be armed.");
        audio_source_name = name;
        last_heartbeat_ns.store(os_gettime_ns());
        AuditScene(); // Applies the single approved track; all other routes remain zero.
        obs_data_set_bool(response, "armed", true);
    } catch (const std::exception& error) {
        module_guard.Disarm(); AuditScene();
        obs_data_set_bool(response, "armed", false); obs_data_set_string(response, "error", error.what());
    }
}
void Disarm(obs_data_t*, obs_data_t* response, void*) { StopPublication(); obs_data_set_bool(response, "armed", false); }
void Heartbeat(obs_data_t*, obs_data_t* response, void*) { last_heartbeat_ns.store(os_gettime_ns()); obs_data_set_bool(response, "armed", module_guard.Active()); }
}
void RegisterVendorRequests() {
    vendor = obs_websocket_register_vendor("jkiller");
    if (!vendor) { blog(LOG_ERROR, "[JKiller] private vendor control unavailable; publishing remains disarmed"); return; }
    obs_websocket_vendor_register_request(vendor, "Arm", Arm, nullptr);
    obs_websocket_vendor_register_request(vendor, "Disarm", Disarm, nullptr);
    obs_websocket_vendor_register_request(vendor, "Heartbeat", Heartbeat, nullptr);
    obs_websocket_vendor_register_request(vendor, "GetSafetyStatus", Status, nullptr);
}
void UnregisterVendorRequests() {
    if (!vendor) return;
    for (const char* name : {"Arm", "Disarm", "Heartbeat", "GetSafetyStatus"}) obs_websocket_vendor_unregister_request(vendor, name);
}
}
