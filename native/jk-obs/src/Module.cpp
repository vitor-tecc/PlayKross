#include "ModuleState.hpp"
#include <obs-frontend-api.h>
#include <util/platform.h>

OBS_DECLARE_MODULE()
MODULE_EXPORT const char* obs_module_description(void) { return "JKiller process-only audio and publication guard"; }

namespace jk {
SceneGuard module_guard;
std::atomic<std::uint64_t> last_heartbeat_ns{0};
namespace {
std::atomic_bool stopping{false};
bool Allowed(obs_source_t* source) {
    obs_data_t* settings = obs_source_get_settings(source);
    const auto generation = static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation"));
    obs_data_release(settings);
    return SourceAllowed(source, generation);
}
void Mixers(void*, calldata_t* data) {
    auto* source = static_cast<obs_source_t*>(calldata_ptr(data, "source"));
    if (!Allowed(source)) calldata_set_int(data, "mixers", 0); // OBS reads this back before applying the route.
}
void Monitoring(void*, calldata_t* data) {
    if (calldata_int(data, "type") != OBS_MONITORING_TYPE_NONE && module_guard.Active()) StopPublication();
}
void HookSource(void*, calldata_t* data) {
    auto* source = static_cast<obs_source_t*>(calldata_ptr(data, "source"));
    auto* signals = obs_source_get_signal_handler(source);
    signal_handler_connect(signals, "audio_mixers", Mixers, nullptr);
    signal_handler_connect(signals, "audio_monitoring", Monitoring, nullptr);
    if (obs_source_get_output_flags(source) & OBS_SOURCE_AUDIO) obs_source_set_audio_mixers(source, Allowed(source) ? 1 : 0);
}
bool Audit(void* result, obs_source_t* source) {
    if (!(obs_source_get_output_flags(source) & OBS_SOURCE_AUDIO)) return true;
    const bool allowed = Allowed(source);
    if (allowed) {
        obs_data_t* settings = obs_source_get_settings(source);
        const auto generation = static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation"));
        obs_data_release(settings);
        if (!AudioSourceReady(obs_source_get_name(source), generation, 0)) *static_cast<bool*>(result) = false;
    }
    const auto expected = allowed ? 1u : 0u;
    if (obs_source_get_audio_mixers(source) != expected) { *static_cast<bool*>(result) = false; obs_source_set_audio_mixers(source, expected); }
    if (obs_source_get_monitoring_type(source) != OBS_MONITORING_TYPE_NONE) *static_cast<bool*>(result) = false;
    return true;
}
void Tick(void*, float) {
    if (module_guard.Active()) {
        const auto heartbeat = last_heartbeat_ns.load();
        if (!heartbeat || os_gettime_ns() - heartbeat > 2000000000ULL || !AuditScene()) StopPublication();
    } else if (obs_frontend_streaming_active()) StopPublication();
}
void FrontendEvent(obs_frontend_event event, void*) {
    if (event == OBS_FRONTEND_EVENT_FINISHED_LOADING)
        obs_queue_task(OBS_TASK_UI, [](void*) { StartLocalControl(); }, nullptr, false);
}
}
bool SourceAllowed(obs_source_t* source, std::uint64_t generation) {
    if (!source) return false;
    return module_guard.Allows({obs_source_get_unversioned_id(source), obs_source_get_name(source), generation, obs_source_get_monitoring_type(source) != OBS_MONITORING_TYPE_NONE});
}
bool AuditScene() { bool safe = true; obs_enum_sources(Audit, &safe); return safe; }
void StopPublication() {
    module_guard.Disarm();
    AuditScene();
    if (!stopping.exchange(true)) obs_queue_task(OBS_TASK_UI, [](void*) { StopAllAudioSources(); obs_frontend_streaming_stop(); stopping.store(false); }, nullptr, false);
}
}
bool obs_module_load(void) {
    jk::RegisterAudioSource();
    signal_handler_connect(obs_get_signal_handler(), "source_create", jk::HookSource, nullptr);
    obs_add_tick_callback(jk::Tick, nullptr);
    blog(LOG_INFO, "[JKiller] process-only audio source and synchronous route guard loaded");
    return true;
}
void obs_module_post_load(void) { jk::RegisterVendorRequests(); obs_frontend_add_event_callback(jk::FrontendEvent, nullptr); }
void obs_module_unload(void) {
    jk::StopLocalControl(); jk::module_guard.Disarm(); jk::StopAllAudioSources(); jk::UnregisterVendorRequests();
    obs_frontend_remove_event_callback(jk::FrontendEvent, nullptr);
    obs_remove_tick_callback(jk::Tick, nullptr);
    signal_handler_disconnect(obs_get_signal_handler(), "source_create", jk::HookSource, nullptr);
}
