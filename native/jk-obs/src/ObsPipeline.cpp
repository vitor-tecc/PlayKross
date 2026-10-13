#include "ModuleState.hpp"
#include <mutex>
#include <string>
#include <cstring>

namespace jk {
namespace {
std::mutex pipeline_mutex;
obs_output_t* output{};
obs_encoder_t* video{};
obs_encoder_t* audio{};
obs_service_t* service{};
std::uint64_t generation{};
bool initialized = false;
void Release() {
    if (output) { obs_output_force_stop(output); obs_output_release(output); output = nullptr; }
    if (video) { obs_encoder_release(video); video = nullptr; }
    if (audio) { obs_encoder_release(audio); audio = nullptr; }
    if (service) { obs_service_release(service); service = nullptr; }
    generation = 0; initialized = false;
}
void Capabilities(obs_data_t*, obs_data_t* response, void*) {
    auto* encoders = obs_data_array_create(); auto* sources = obs_data_array_create(); auto* services = obs_data_array_create();
    const char* id{};
    for (size_t i = 0; obs_enum_encoder_types(i, &id); ++i) {
        auto* item = obs_data_create(); obs_data_set_string(item, "id", id); obs_data_set_string(item, "codec", obs_get_encoder_codec(id));
        obs_data_set_int(item, "caps", obs_get_encoder_caps(id)); obs_data_array_push_back(encoders, item); obs_data_release(item);
    }
    for (size_t i = 0; obs_enum_input_types(i, &id); ++i) { auto* item = obs_data_create(); obs_data_set_string(item, "id", id); obs_data_array_push_back(sources, item); obs_data_release(item); }
    for (size_t i = 0; obs_enum_service_types(i, &id); ++i) { auto* item = obs_data_create(); obs_data_set_string(item, "id", id); obs_data_array_push_back(services, item); obs_data_release(item); }
    obs_data_set_array(response, "encoders", encoders); obs_data_set_array(response, "sources", sources); obs_data_set_array(response, "services", services);
    obs_data_array_release(encoders); obs_data_array_release(sources); obs_data_array_release(services);
}
void Describe(obs_data_t* response) {
    obs_data_set_bool(response, "success", initialized);
    obs_data_set_bool(response, "outputActive", output && obs_output_active(output));
    if (!video) return;
    const char* id = obs_encoder_get_id(video);
    obs_data_set_string(response, "encoder", id); obs_data_set_string(response, "codec", obs_encoder_get_codec(video));
    obs_data_set_bool(response, "hardwareInitialized", initialized && (strcmp(id, "obs_nvenc_h264_tex") == 0 || strcmp(id, "obs_nvenc_h264_soft") == 0));
    auto* settings = obs_encoder_get_settings(video);
    obs_data_set_int(response, "bitrateKbps", obs_data_get_int(settings, "bitrate"));
    obs_data_set_int(response, "bFrames", obs_data_get_int(settings, "bf")); obs_data_set_bool(response, "lookahead", obs_data_get_bool(settings, "lookahead"));
    obs_data_release(settings);
    if (output) {
        obs_data_set_int(response, "sentMediaBytes", static_cast<long long>(obs_output_get_total_bytes(output)));
        obs_data_set_int(response, "sentFrames", obs_output_get_total_frames(output));
        obs_data_set_int(response, "droppedFrames", obs_output_get_frames_dropped(output));
        obs_data_set_int(response, "connectionDelayMs", obs_output_get_connect_time_ms(output));
        obs_data_set_string(response, "outputError", obs_output_get_last_error(output));
    }
}
void Prepare(obs_data_t* request, obs_data_t* response, void*) {
    std::lock_guard lock(pipeline_mutex);
    if (output && obs_output_active(output)) { obs_data_set_bool(response, "success", false); obs_data_set_string(response, "error", "Stop the active publication before changing encoder settings."); return; }
    Release();
    const std::string id = obs_data_get_string(request, "encoder");
    const auto bitrate = obs_data_get_int(request, "bitrateKbps");
    const auto requested_generation = obs_data_get_int(request, "generation");
    if ((id != "obs_nvenc_h264_tex" && id != "obs_x264") || bitrate < 1000 || bitrate > 25000 || bitrate % 500 || requested_generation <= 0) {
        obs_data_set_bool(response, "success", false); obs_data_set_string(response, "error", "Invalid typed encoder configuration."); return;
    }
    auto* settings = obs_data_create();
    obs_data_set_int(settings, "bitrate", bitrate); obs_data_set_int(settings, "max_bitrate", bitrate); obs_data_set_int(settings, "keyint_sec", 1);
    obs_data_set_int(settings, "bf", 0); obs_data_set_bool(settings, "lookahead", false); obs_data_set_string(settings, "rate_control", "CBR");
    obs_data_set_string(settings, "profile", "high");
    if (id == "obs_x264") { obs_data_set_string(settings, "preset", "veryfast"); obs_data_set_string(settings, "tune", "zerolatency"); obs_data_set_string(settings, "x264opts", "bframes=0:rc-lookahead=0:sync-lookahead=0"); }
    else { obs_data_set_string(settings, "preset", "p4"); obs_data_set_string(settings, "tune", "ll"); obs_data_set_string(settings, "multipass", "disabled"); obs_data_set_bool(settings, "adaptive_quantization", false); }
    video = obs_video_encoder_create(id.c_str(), "JK Shared Video", settings, nullptr); obs_data_release(settings);
    auto* audio_settings = obs_data_create(); obs_data_set_int(audio_settings, "bitrate", 128);
    audio = obs_audio_encoder_create("ffmpeg_opus", "JK Application Opus", audio_settings, 0, nullptr); obs_data_release(audio_settings);
    output = obs_output_create("whip_output", "JK Shared WHIP", nullptr, nullptr);
    if (video && audio && output) {
        obs_encoder_set_video(video, obs_get_video()); obs_encoder_set_audio(audio, obs_get_audio());
        obs_output_set_video_encoder(output, video); obs_output_set_audio_encoder(output, audio, 0);
        initialized = obs_output_initialize_encoders(output, 0);
    }
    generation = static_cast<std::uint64_t>(requested_generation);
    Describe(response);
    if (!initialized) { obs_data_set_string(response, "error", "Selected H.264/Opus encoder could not initialize; no automatic CPU fallback."); Release(); }
}
void Start(obs_data_t* request, obs_data_t* response, void*) {
    std::lock_guard lock(pipeline_mutex);
    if (!initialized || !output || generation != static_cast<std::uint64_t>(obs_data_get_int(request, "generation")) || !module_guard.Active() || !GuardVideoReady() || !AuditScene()) {
        obs_data_set_bool(response, "success", false); obs_data_set_string(response, "error", "Native pipeline or original selection is not ready."); return;
    }
    auto* settings = obs_data_create(); obs_data_set_string(settings, "server", obs_data_get_string(request, "url")); obs_data_set_string(settings, "bearer_token", obs_data_get_string(request, "token"));
    if (service) obs_service_release(service);
    service = obs_service_create_private("whip_custom", "JK Private Service", settings); obs_data_release(settings);
    if (service) obs_output_set_service(output, service);
    obs_data_set_bool(response, "success", service && obs_output_start(output));
}
void Stop(obs_data_t*, obs_data_t* response, void*) { StopPipeline(); obs_data_set_bool(response, "success", true); }
void State(obs_data_t*, obs_data_t* response, void*) { DescribePipeline(response); }
}
void StopPipeline() { std::lock_guard lock(pipeline_mutex); if (output) obs_output_force_stop(output); }
void ReleasePipeline() { std::lock_guard lock(pipeline_mutex); Release(); }
void DescribePipeline(obs_data_t* response) { std::lock_guard lock(pipeline_mutex); Describe(response); }
void RegisterPipelineRequests(obs_websocket_vendor vendor) {
    obs_websocket_vendor_register_request(vendor, "GetCapabilities", Capabilities, nullptr);
    obs_websocket_vendor_register_request(vendor, "PreparePipeline", Prepare, nullptr);
    obs_websocket_vendor_register_request(vendor, "StartPipeline", Start, nullptr);
    obs_websocket_vendor_register_request(vendor, "StopPipeline", Stop, nullptr);
    obs_websocket_vendor_register_request(vendor, "GetPipelineState", State, nullptr);
}
void UnregisterPipelineRequests(obs_websocket_vendor vendor) { for (const char* name : {"GetCapabilities", "PreparePipeline", "StartPipeline", "StopPipeline", "GetPipelineState"}) obs_websocket_vendor_unregister_request(vendor, name); }
}
