#pragma once
// Test-only OBS and capture boundaries. No OBS runtime or device is loaded.
#include "SceneGuard.hpp"
#include <atomic>
#include <functional>
#include <map>
#include <vector>
#include <stdexcept>
using obs_websocket_vendor = void*;
struct obs_data_t {
    std::map<std::string, std::string> strings;
    std::map<std::string, long long> ints;
    std::map<std::string, bool> bools;
    std::map<std::string, double> doubles;
};
struct obs_source_t {
    std::string name, type = "jk_process_audio";
    unsigned mixers = 0, flags = 1;
    int monitoring = 0;
};
struct calldata_t {};
struct obs_source_audio {
    const std::uint8_t* data[8]{};
    std::uint32_t frames{}, samples_per_sec{};
    int speakers{}, format{};
    std::uint64_t timestamp{};
};
struct obs_source_info {
    const char* id{}; int type{}; unsigned output_flags{};
    const char* (*get_name)(void*){};
    void* (*create)(obs_data_t*, obs_source_t*){};
    void (*destroy)(void*){};
    void (*update)(void*, obs_data_t*){};
};
inline std::vector<obs_source_t*> harness_sources;
inline void obs_data_set_string(obs_data_t* d, const char* k, const char* v) { d->strings[k] = v; }
inline void obs_data_set_int(obs_data_t* d, const char* k, long long v) { d->ints[k] = v; }
inline void obs_data_set_bool(obs_data_t* d, const char* k, bool v) { d->bools[k] = v; }
inline void obs_data_set_double(obs_data_t* d, const char* k, double v) { d->doubles[k] = v; }
inline const char* obs_data_get_string(obs_data_t* d, const char* k) { return d->strings[k].c_str(); }
inline long long obs_data_get_int(obs_data_t* d, const char* k) { return d->ints[k]; }
inline bool obs_data_get_bool(obs_data_t* d, const char* k) { return d->bools[k]; }
inline const char* obs_source_get_name(obs_source_t* s) { return s->name.c_str(); }
inline const char* obs_source_get_unversioned_id(obs_source_t* s) { return s->type.c_str(); }
inline unsigned obs_source_get_output_flags(obs_source_t* s) { return s->flags; }
inline unsigned obs_source_get_audio_mixers(obs_source_t* s) { return s->mixers; }
inline void obs_source_set_audio_mixers(obs_source_t* s, unsigned v) { s->mixers = v; }
inline int obs_source_get_monitoring_type(obs_source_t* s) { return s->monitoring; }
inline obs_source_t* obs_source_get_ref(obs_source_t* s) { return s; }
inline void obs_source_release(obs_source_t*) {}
inline void obs_source_output_audio(obs_source_t*, obs_source_audio*) {}
inline void obs_register_source(obs_source_info*) {}
inline void obs_enum_sources(bool (*callback)(void*, obs_source_t*), void* data) {
    for (auto* source : harness_sources) if (!callback(data, source)) break;
}
inline void* obs_source_get_signal_handler(obs_source_t*) { return nullptr; }
inline void* obs_get_signal_handler() { return nullptr; }
inline void signal_handler_connect(void*, const char*, void (*)(void*, calldata_t*), void*) {}
inline void signal_handler_disconnect(void*, const char*, void (*)(void*, calldata_t*), void*) {}
inline void* calldata_ptr(calldata_t*, const char*) { return nullptr; }
inline long long calldata_int(calldata_t*, const char*) { return 0; }
inline void calldata_set_int(calldata_t*, const char*, long long) {}
inline void obs_add_tick_callback(void (*)(void*, float), void*) {}
inline void obs_remove_tick_callback(void (*)(void*, float), void*) {}
inline void obs_queue_task(int, void (*)(void*), void*, bool) {}
inline void blog(int, const char*, ...) {}
inline constexpr int LOG_WARNING = 1, LOG_INFO = 2, OBS_TASK_UI = 1;
inline constexpr unsigned OBS_SOURCE_AUDIO = 1;
inline constexpr int OBS_MONITORING_TYPE_NONE = 0, SPEAKERS_STEREO = 2, AUDIO_FORMAT_FLOAT = 1, OBS_SOURCE_TYPE_INPUT = 1;
#define OBS_DECLARE_MODULE()
#define MODULE_EXPORT
namespace jk {
struct AudioQueueStats { double queued_ms; std::uint64_t dropped_frames; };
struct CaptureResult { bool started; std::string error; };
struct PcmBlock { std::vector<float> samples; std::uint32_t channels, sample_rate; std::uint64_t monotonic_ns; };
struct AudioSink { virtual ~AudioSink() = default; virtual void OnPcm(const PcmBlock&) = 0; };
class ProcessLoopback {
public:
    bool running = true;
    CaptureResult Start(ProcessBinding&, AudioSink&) { return {true, ""}; }
    void Stop() { running = false; }
    bool Running() const { return running; }
    std::string Error() const { return ""; }
    AudioQueueStats QueueStats() const { return {12.5, 96}; }
};
class EndpointLoopback : public ProcessLoopback {
public:
    CaptureResult Start(const std::wstring&, ProcessBinding&, AudioSink&) { return {true,""}; }
};
extern SceneGuard module_guard;
extern std::atomic<std::uint64_t> last_heartbeat_ns;
void RegisterAudioSource();
inline void RegisterVideoSource() {}
inline void StartLocalControl() {}
inline void StopLocalControl() {}
inline void RegisterVendorRequests() {}
inline void UnregisterVendorRequests() {}
inline void ReleasePipeline() {}
inline void StopPipeline() {}
inline bool ControllerLost() { return false; }
inline bool GuardVideoReady() { return true; }
inline std::optional<ProcessToken> ControllerIdentity() { return std::nullopt; }
bool SourceAllowed(obs_source_t*, std::uint64_t);
bool AuditScene();
void StopPublication(GuardStopReason reason = GuardStopReason::None);
void StopAllAudioSources();
void DescribeAudioSource(const AudioRoute&, obs_data_t*);
void DescribeSelectedAudio(obs_data_t*);
std::optional<AudioRoute> SelectedAudioRoute(const char*, std::uint64_t, std::uint32_t);
bool AudioSourceReady(const char*, std::uint64_t, std::uint32_t);
}
