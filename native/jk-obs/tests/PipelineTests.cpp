#include "../src/ObsPipeline.cpp"
#include <iostream>
#include <stdexcept>
static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
struct PipelineFixture {
    obs_data_t response;
    PipelineFixture() { jk::ReleasePipeline(); pipeline_harness::initialize_succeeds = true; pipeline_harness::fail_audio = false; pipeline_harness::attempts.clear(); }
    ~PipelineFixture() { jk::ReleasePipeline(); }
    obs_data_t* Prepare(const char* id, int bitrate = 4000, int generation = 1) {
        obs_data_t request; response = {};
        obs_data_set_string(&request, "encoder", id); obs_data_set_int(&request, "bitrateKbps", bitrate); obs_data_set_int(&request, "generation", generation);
        jk::Prepare(&request, &response, nullptr); return &response;
    }
    void Start() { obs_data_t request, reply; obs_data_set_int(&request, "generation", 1); jk::Start(&request, &reply, nullptr); Require(obs_data_get_bool(&reply, "success"), "start failed"); }
    obs_data_t* Settings() { return &pipeline_harness::last_settings; }
    void Empty() { Require(pipeline_harness::video_count == 0 && pipeline_harness::audio_count == 0 && pipeline_harness::output_count == 0 && pipeline_harness::service_count == 0 && pipeline_harness::callbacks == 0 && pipeline_harness::renders == 0, "resource leak"); }
};
int main(int argc, char** argv) try {
    if (argc != 2) return 2;
    const std::string name = argv[1]; PipelineFixture f;
    if (name == "AmfPolicyAndInitialization" || name == "RerouteDoesNotProveTextureTransfer") {
        auto* reply = f.Prepare("h264_texture_amf");
        Require(obs_data_get_bool(reply, "success"), "AMF preparation rejected");
        Require(obs_data_get_bool(reply, "hardwareInitialized"), "AMF hardware not initialized");
        Require(std::string(obs_data_get_string(reply, "encoderFamily")) == "amf", "AMF family unavailable");
        Require(std::string(obs_data_get_string(reply, "encoderTransferRoute")) == "unverified", "original ID cannot prove transfer route");
        Require(std::string(obs_data_get_string(f.Settings(), "preset")) == "speed", "AMF preset");
        Require(!obs_data_get_bool(f.Settings(), "pre_analysis"), "AMF analysis");
        Require(std::string(obs_data_get_string(f.Settings(), "ffmpeg_opts")) == "usage=lowlatency preanalysis=false vbaq=false", "AMF expensive analysis not disabled");
        Require(obs_data_get_int(reply, "bFrames") == 0 && obs_data_get_bool(reply, "repeatHeaders"), "late-join policy");
        Require(obs_data_get_int(f.Settings(), "keyint_sec") == 1 && std::string(obs_data_get_string(f.Settings(), "rate_control")) == "CBR", "stream policy");
    } else if (name == "FailedAttemptReleasesAllResources") {
        pipeline_harness::initialize_succeeds = false;
        auto* reply = f.Prepare("obs_nvenc_h264_tex");
        Require(!obs_data_get_bool(reply, "success") && !obs_data_get_bool(reply, "hardwareInitialized"), "failed initialization was reported as working"); f.Empty();
        pipeline_harness::initialize_succeeds = true;
        Require(obs_data_get_bool(f.Prepare("h264_texture_amf"), "success"), "retry after failure");
        jk::ReleasePipeline(); f.Empty();
        pipeline_harness::fail_audio = true; f.Prepare("obs_nvenc_h264_tex"); f.Empty();
    } else if (name == "ActiveOutputCannotBeReconfigured") {
        f.Prepare("obs_nvenc_h264_tex"); f.Start();
        Require(!obs_data_get_bool(f.Prepare("h264_texture_amf"), "success"), "active output replaced");
        Require(pipeline_harness::attempts.size() == 1 && pipeline_harness::video_count == 1, "active encoder disturbed");
    } else if (name == "InvalidTypedRequestCreatesNothing") {
        for (int rate : {500, 1250, 25500}) { Require(!obs_data_get_bool(f.Prepare("h264_texture_amf", rate), "success"), "invalid bitrate accepted"); f.Empty(); }
        for (int gen : {0, -1}) { Require(!obs_data_get_bool(f.Prepare("h264_texture_amf", 4000, gen), "success"), "invalid generation accepted"); f.Empty(); }
        Require(!obs_data_get_bool(f.Prepare("unsupported"), "success"), "invalid ID accepted"); f.Empty();
        for (int rate : {1000, 25000}) Require(obs_data_get_bool(f.Prepare("h264_texture_amf", rate), "success"), "valid boundary rejected");
    } else if (name == "NvencAndX264PolicyPreserved") {
        Require(obs_data_get_bool(f.Prepare("obs_nvenc_h264_tex"), "success"), "NVENC rejected");
        Require(std::string(obs_data_get_string(f.Settings(), "preset")) == "p4" && !obs_data_get_bool(f.Settings(), "lookahead"), "NVENC policy");
        auto* reply = f.Prepare("obs_x264"); Require(obs_data_get_bool(reply, "success") && !obs_data_get_bool(reply, "hardwareInitialized"), "x264 is software");
        Require(std::string(obs_data_get_string(f.Settings(), "preset")) == "veryfast" && std::string(obs_data_get_string(f.Settings(), "tune")) == "zerolatency", "x264 policy");
    } else return 2;
    jk::ReleasePipeline(); f.Empty(); std::cout << name << ": passed\n"; return 0;
} catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
