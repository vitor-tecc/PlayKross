#pragma once
#include <cstdint>
#include <map>
#include <string>
#include <vector>
struct obs_data_t {
    std::map<std::string, std::string> strings;
    std::map<std::string, long long> ints;
    std::map<std::string, bool> bools;
    std::map<std::string, double> doubles;
};
struct obs_data_array_t {};
inline obs_data_t* obs_data_create() { return new obs_data_t; }
inline void obs_data_release(obs_data_t* d) { delete d; }
inline void obs_data_set_string(obs_data_t* d, const char* k, const char* v) { d->strings[k] = v ? v : ""; }
inline void obs_data_set_int(obs_data_t* d, const char* k, long long v) { d->ints[k] = v; }
inline void obs_data_set_bool(obs_data_t* d, const char* k, bool v) { d->bools[k] = v; }
inline void obs_data_set_double(obs_data_t* d, const char* k, double v) { d->doubles[k] = v; }
inline const char* obs_data_get_string(obs_data_t* d, const char* k) { return d->strings[k].c_str(); }
inline long long obs_data_get_int(obs_data_t* d, const char* k) { return d->ints[k]; }
inline bool obs_data_get_bool(obs_data_t* d, const char* k) { return d->bools[k]; }
inline obs_data_array_t* obs_data_array_create() { return new obs_data_array_t; }
inline void obs_data_array_release(obs_data_array_t* a) { delete a; }
inline void obs_data_array_push_back(obs_data_array_t*, obs_data_t*) {}
inline void obs_data_set_array(obs_data_t*, const char*, obs_data_array_t*) {}
inline bool obs_enum_encoder_types(size_t, const char**) { return false; }
inline bool obs_enum_input_types(size_t, const char**) { return false; }
inline bool obs_enum_service_types(size_t, const char**) { return false; }
inline const char* obs_get_encoder_codec(const char*) { return "h264"; }
inline int obs_get_encoder_caps(const char*) { return 0; }
struct obs_encoder_t { std::string id; obs_data_t settings; bool active{}; };
struct obs_service_t {};
struct obs_output_t { obs_encoder_t* video{}; obs_encoder_t* audio{}; bool active{}, callback{}; };
struct encoder_packet { int type{}; long long pts{}; };
struct encoder_packet_time { std::uint64_t fer{}, ferc{}, cts{}; };
struct obs_video_info { int fps_num = 30, fps_den = 1; };
namespace pipeline_harness {
inline bool initialize_succeeds = true, fail_audio = false;
inline int video_count{}, audio_count{}, output_count{}, service_count{}, callbacks{}, renders{};
inline obs_data_t last_settings;
inline std::vector<std::string> attempts;
}
inline obs_encoder_t* obs_video_encoder_create(const char* id, const char*, obs_data_t* d, void*) {
    ++pipeline_harness::video_count; pipeline_harness::last_settings = *d;
    pipeline_harness::attempts.emplace_back(id); return new obs_encoder_t{id, *d};
}
inline obs_encoder_t* obs_audio_encoder_create(const char*, const char*, obs_data_t* d, int, void*) {
    if (pipeline_harness::fail_audio) return nullptr;
    ++pipeline_harness::audio_count; return new obs_encoder_t{"ffmpeg_opus", *d};
}
inline void obs_encoder_release(obs_encoder_t* e) {
    if (e->id == "ffmpeg_opus") --pipeline_harness::audio_count; else --pipeline_harness::video_count;
    delete e;
}
inline const char* obs_encoder_get_id(obs_encoder_t* e) { return e->id.c_str(); }
inline const char* obs_encoder_get_codec(obs_encoder_t* e) { return e->id == "ffmpeg_opus" ? "opus" : "h264"; }
inline bool obs_encoder_active(obs_encoder_t* e) { return e->active; }
inline obs_data_t* obs_encoder_get_settings(obs_encoder_t* e) { return new obs_data_t(e->settings); }
inline void* obs_get_video() { static int v; return &v; }
inline void* obs_get_audio() { return obs_get_video(); }
inline void obs_encoder_set_video(obs_encoder_t*, void*) {}
inline void obs_encoder_set_audio(obs_encoder_t*, void*) {}
inline int obs_encoder_get_width(obs_encoder_t*) { return 1280; }
inline int obs_encoder_get_height(obs_encoder_t*) { return 720; }
inline int obs_encoder_get_encoded_frames(obs_encoder_t*) { return 0; }
inline obs_output_t* obs_output_create(const char*, const char*, void*, void*) {
    ++pipeline_harness::output_count; return new obs_output_t;
}
inline bool obs_output_active(obs_output_t* o) { return o->active; }
inline void obs_output_force_stop(obs_output_t* o) { o->active = false; }
inline void obs_output_release(obs_output_t* o) { --pipeline_harness::output_count; delete o; }
using packet_callback = void (*)(obs_output_t*, encoder_packet*, encoder_packet_time*, void*);
inline void obs_output_add_packet_callback(obs_output_t* o, packet_callback, void*) { o->callback = true; ++pipeline_harness::callbacks; }
inline void obs_output_remove_packet_callback(obs_output_t* o, packet_callback, void*) { if (o->callback) { --pipeline_harness::callbacks; o->callback = false; } }
inline void obs_add_main_render_callback(void (*)(void*, std::uint32_t, std::uint32_t), void*) { ++pipeline_harness::renders; }
inline void obs_remove_main_render_callback(void (*)(void*, std::uint32_t, std::uint32_t), void*) { --pipeline_harness::renders; }
inline void obs_add_main_rendered_callback(void (*)(void*), void*) { ++pipeline_harness::renders; }
inline void obs_remove_main_rendered_callback(void (*)(void*), void*) { --pipeline_harness::renders; }
inline void obs_output_set_reconnect_settings(obs_output_t*, int, int) {}
inline void obs_output_set_video_encoder(obs_output_t* o, obs_encoder_t* e) { o->video = e; }
inline void obs_output_set_audio_encoder(obs_output_t* o, obs_encoder_t* e, int) { o->audio = e; }
inline bool obs_output_initialize_encoders(obs_output_t*, int) { return pipeline_harness::initialize_succeeds; }
inline bool obs_output_start(obs_output_t* o) { o->active = true; o->video->active = true; o->audio->active = true; return true; }
inline long long obs_output_get_total_bytes(obs_output_t*) { return 0; }
inline int obs_output_get_total_frames(obs_output_t*) { return 0; }
inline int obs_output_get_frames_dropped(obs_output_t*) { return 0; }
inline int obs_output_get_connect_time_ms(obs_output_t*) { return 0; }
inline const char* obs_output_get_last_error(obs_output_t*) { return ""; }
inline obs_service_t* obs_service_create_private(const char*, const char*, obs_data_t*) { ++pipeline_harness::service_count; return new obs_service_t; }
inline void obs_service_release(obs_service_t* s) { --pipeline_harness::service_count; delete s; }
inline void obs_output_set_service(obs_output_t*, obs_service_t*) {}
inline long long obs_get_average_frame_time_ns() { return 0; }
inline int obs_get_total_frames() { return 0; }
inline int obs_get_lagged_frames() { return 0; }
inline int video_output_get_total_frames(void*) { return 0; }
inline int video_output_get_skipped_frames(void*) { return 0; }
inline bool obs_get_video_info(obs_video_info*) { return true; }
inline constexpr int OBS_ENCODER_VIDEO = 1, LOG_INFO = 1;
inline void blog(int, const char*, ...) {}
using obs_websocket_vendor = void*;
inline void obs_websocket_vendor_register_request(obs_websocket_vendor, const char*, void (*)(obs_data_t*, obs_data_t*, void*), void*) {}
inline void obs_websocket_vendor_unregister_request(obs_websocket_vendor, const char*) {}
namespace jk {
inline struct Guard { bool Active() const { return true; } } module_guard;
inline bool GuardVideoReady() { return true; }
inline bool AuditScene() { return true; }
inline void DescribeVideo(obs_data_t*) {}
inline void DescribeSelectedAudio(obs_data_t*) {}
void StopPipeline();
void ReleasePipeline();
void DescribePipeline(obs_data_t*);
}
