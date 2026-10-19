#include "ModuleState.hpp"
#include <mutex>
#include <string>
#include <cstring>
#include "NativeDiagnostics.hpp"
#include <util/platform.h>

namespace jk {
namespace {
std::mutex pipeline_mutex;
obs_output_t* output{};
obs_encoder_t* video{};
obs_encoder_t* audio{};
obs_service_t* service{};
std::uint64_t generation{};
bool initialized = false;
bool diagnostics_callbacks = false;
void BeginComposition(void*, std::uint32_t, std::uint32_t) { BeginDiagnosticsComposition(); }
void EndComposition(void*) { EndDiagnosticsComposition(); }
void PacketObserved(obs_output_t*, encoder_packet* packet, encoder_packet_time* timing, void*) {
    if (!DiagnosticsEnabled() || !packet || packet->type != OBS_ENCODER_VIDEO) return;
    auto& trace = DiagnosticsTrace(); const auto now = os_gettime_ns();
    if (timing && timing->fer && timing->fer <= now) {
        trace.RemapComposition(static_cast<std::int64_t>(timing->cts), packet->pts);
        trace.EncodeInput(packet->pts, timing->fer);
        if (timing->ferc) trace.EncodeReturn(packet->pts, timing->ferc);
    }
    trace.Encoded(packet->pts, now);
}
void Release() {
    if (output) { obs_output_force_stop(output); obs_output_remove_packet_callback(output, PacketObserved, nullptr); obs_output_release(output); output = nullptr; }
    if (diagnostics_callbacks) { obs_remove_main_render_callback(BeginComposition, nullptr); obs_remove_main_rendered_callback(EndComposition, nullptr); diagnostics_callbacks = false; }
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
    // These are only this broadcaster's owned shared pipeline encoders.
    obs_data_set_int(response, "activeVideoEncoders", video && obs_encoder_active(video) ? 1 : 0);
    obs_data_set_int(response, "activeAudioEncoders", audio && obs_encoder_active(audio) ? 1 : 0);
    obs_data_set_bool(response, "hardwareInitialized", false);
    if (!video) return;
    const char* id = obs_encoder_get_id(video);
    const bool amf = strcmp(id, "h264_texture_amf") == 0;
    const bool nvenc = strcmp(id, "obs_nvenc_h264_tex") == 0 || strcmp(id, "obs_nvenc_h264_soft") == 0;
    obs_data_set_string(response, "encoder", id); obs_data_set_string(response, "codec", obs_encoder_get_codec(video));
    obs_data_set_bool(response, "hardwareInitialized", initialized && (nvenc || amf));
    obs_data_set_string(response, "encoderFamily", amf ? "amf" : nvenc ? "nvenc" : "x264");
    // OBS returns orig_info for ID/caps even after AMF's internal frame-upload
    // reroute. Initialization proves the encoder family, never zero-copy input.
    obs_data_set_string(response, "encoderTransferRoute", "unverified");
    auto* settings = obs_encoder_get_settings(video);
    obs_data_set_int(response, "bitrateKbps", obs_data_get_int(settings, "bitrate"));
    obs_data_set_int(response, "bFrames", obs_data_get_int(settings, "bf")); obs_data_set_bool(response, "lookahead", obs_data_get_bool(settings, "lookahead"));
    obs_data_set_bool(response, "repeatHeaders", obs_data_get_bool(settings, "repeat_headers"));
    obs_data_set_string(response, "preset", obs_data_get_string(settings, "preset"));
    obs_data_set_bool(response, "preAnalysis", obs_data_get_bool(settings, "pre_analysis"));
    obs_data_set_string(response, "amfOptions", obs_data_get_string(settings, "ffmpeg_opts"));
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
    if ((id != "obs_nvenc_h264_tex" && id != "h264_texture_amf" && id != "obs_x264") || bitrate < 1000 || bitrate > 25000 || bitrate % 500 || requested_generation <= 0) {
        obs_data_set_bool(response, "success", false); obs_data_set_string(response, "error", "Invalid typed encoder configuration."); return;
    }
    auto* settings = obs_data_create();
    obs_data_set_int(settings, "bitrate", bitrate); obs_data_set_int(settings, "max_bitrate", bitrate); obs_data_set_int(settings, "keyint_sec", 1);
    obs_data_set_int(settings, "bf", 0); obs_data_set_bool(settings, "lookahead", false); obs_data_set_string(settings, "rate_control", "CBR");
    // Match OBS WHIPService::ApplyEncoderSettings before encoder initialization.
    // Browser readers joining later need SPS/PPS on subsequent keyframes too.
    obs_data_set_bool(settings, "repeat_headers", true);
    obs_data_set_string(settings, "profile", "high");
    if (id == "obs_x264") { obs_data_set_string(settings, "preset", "veryfast"); obs_data_set_string(settings, "tune", "zerolatency"); obs_data_set_string(settings, "x264opts", "bframes=0:rc-lookahead=0:sync-lookahead=0"); }
    else if (id == "h264_texture_amf") {
        obs_data_set_string(settings, "preset", "speed");
        obs_data_set_bool(settings, "pre_analysis", false);
        // OBS 32.2.2's preanalysis option controls PREENCODE_ENABLE, distinct
        // from pre_analysis. Override both expensive defaults before Init.
        obs_data_set_string(settings, "ffmpeg_opts", "usage=lowlatency preanalysis=false vbaq=false");
    }
    else { obs_data_set_string(settings, "preset", "p4"); obs_data_set_string(settings, "tune", "ll"); obs_data_set_string(settings, "multipass", "disabled"); obs_data_set_bool(settings, "adaptive_quantization", false); }
    video = obs_video_encoder_create(id.c_str(), "JK Shared Video", settings, nullptr); obs_data_release(settings);
    auto* audio_settings = obs_data_create(); obs_data_set_int(audio_settings, "bitrate", 128);
    audio = obs_audio_encoder_create("ffmpeg_opus", "JK Application Opus", audio_settings, 0, nullptr); obs_data_release(audio_settings);
    output = obs_output_create("whip_output", "JK Shared WHIP", nullptr, nullptr);
    if (video && audio && output) {
        obs_output_add_packet_callback(output, PacketObserved, nullptr);
        obs_add_main_render_callback(BeginComposition, nullptr); obs_add_main_rendered_callback(EndComposition, nullptr); diagnostics_callbacks = true;
        // The typed controller must reacquire a fresh grant before reconnecting.
        // OBS defaults to retrying the old service token after remote revocation.
        obs_output_set_reconnect_settings(output, 0, 1);
        obs_encoder_set_video(video, obs_get_video()); obs_encoder_set_audio(audio, obs_get_audio());
        obs_output_set_video_encoder(output, video); obs_output_set_audio_encoder(output, audio, 0);
        initialized = obs_output_initialize_encoders(output, 0);
        if (initialized) {
            auto* applied = obs_encoder_get_settings(video);
            blog(LOG_INFO, "[PlayKross] Shared H.264 encoder initialized: %s; repeat_headers=%s", id.c_str(), obs_data_get_bool(applied, "repeat_headers") ? "true" : "false");
            obs_data_release(applied);
        }
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
void Diagnostics(obs_data_t*, obs_data_t* response, void*) {
    std::lock_guard lock(pipeline_mutex); Describe(response); DescribeVideo(response);
    DescribeSelectedAudio(response);
    obs_data_set_bool(response, "traceEnabled", DiagnosticsEnabled());
    obs_data_set_int(response, "monotonicNs", static_cast<long long>(os_gettime_ns()));
    obs_data_set_double(response, "nativeRenderAverageMs", static_cast<double>(obs_get_average_frame_time_ns()) / 1'000'000.0);
    obs_data_set_int(response, "renderClockFrames", obs_get_total_frames()); obs_data_set_int(response, "renderLagFrames", obs_get_lagged_frames());
    if (auto* clock = obs_get_video()) { obs_data_set_int(response,"encodingClockFrames",video_output_get_total_frames(clock)); obs_data_set_int(response,"encodingSkippedFrames",video_output_get_skipped_frames(clock)); }
    if (video) { obs_data_set_int(response, "encodedWidth", obs_encoder_get_width(video)); obs_data_set_int(response, "encodedHeight", obs_encoder_get_height(video)); obs_data_set_int(response, "actualEncodedFrames", obs_encoder_get_encoded_frames(video)); }
    obs_video_info info{}; if (obs_get_video_info(&info)) { obs_data_set_int(response, "requestedFpsNumerator", info.fps_num); obs_data_set_int(response, "requestedFpsDenominator", info.fps_den); }
    obs_data_set_string(response, "gpuKernelTiming", "unavailable: these host timestamps do not measure GPU kernels");
    if (!DiagnosticsEnabled()) { obs_data_set_string(response, "captureUnavailableReason", "Trace disabled for an uninstrumented baseline measurement."); obs_data_set_string(response, "timingProvenance", "Trace timing unavailable in disabled baseline mode."); return; }
    const auto snapshot = DiagnosticsTrace().Snapshot();
    if (obs_data_get_bool(response, "acquisitionHookAvailable")) obs_data_set_int(response, "captureFrames", static_cast<long long>(snapshot.captured_frames));
    else obs_data_set_string(response, "captureUnavailableReason", "The owned libobs-winrt acquisition hook is not installed; OBS render FPS is not capture FPS.");
    obs_data_set_int(response, "compositorFrames", static_cast<long long>(snapshot.rendered_frames));
    obs_data_set_int(response, "repeatedSourceFrames", static_cast<long long>(snapshot.repeated_frames));
    obs_data_set_int(response, "encodedPackets", static_cast<long long>(snapshot.encoded_packets));
    obs_data_set_int(response, "unmatchedPackets", static_cast<long long>(snapshot.unmatched_packets));
    obs_data_set_int(response, "timingSamples", static_cast<long long>(snapshot.sample_count));
    obs_data_set_int(response, "captureTimingSamples", static_cast<long long>(snapshot.capture_timing_samples));
    if (snapshot.encode_residence_ms) obs_data_set_double(response, "inputToOutputPacketMs", *snapshot.encode_residence_ms);
    if (snapshot.capture_to_render_ms) obs_data_set_double(response, "captureToRenderMs", *snapshot.capture_to_render_ms);
    if (snapshot.encode_api_call_ms) obs_data_set_double(response, "encodeApiCallMs", *snapshot.encode_api_call_ms);
    if (snapshot.capture_to_packet_ms) obs_data_set_double(response, "freshCaptureToPacketMs", *snapshot.capture_to_packet_ms);
    obs_data_set_string(response, "timingProvenance", "OBS32.2.2 matched encoder_packet_time CTS/FER/FERC; encode API call and input-to-output-packet residence (includes interleaving), QPC monotonic nanoseconds.");
}
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
    obs_websocket_vendor_register_request(vendor, "GetDiagnostics", Diagnostics, nullptr);
}
void UnregisterPipelineRequests(obs_websocket_vendor vendor) { for (const char* name : {"GetCapabilities", "PreparePipeline", "StartPipeline", "StopPipeline", "GetPipelineState", "GetDiagnostics"}) obs_websocket_vendor_unregister_request(vendor, name); }
}
