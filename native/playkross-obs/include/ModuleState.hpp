#pragma once
#include <obs-module.h>
#include <obs-websocket-api.h>
#include "SceneGuard.hpp"
#include "ProcessLoopback.hpp"
#include "EndpointLoopback.hpp"

namespace jk {
extern SceneGuard module_guard;
extern std::atomic<std::uint64_t> last_heartbeat_ns;
void RegisterAudioSource();
void RegisterVideoSource();
bool VideoSourceReady(const char* name, std::uint64_t generation);
void SelectGuardVideo(std::string name, std::uint64_t generation);
bool GuardVideoReady();
void DescribeVideo(obs_data_t* response);
void RegisterPipelineRequests(obs_websocket_vendor vendor);
void UnregisterPipelineRequests(obs_websocket_vendor vendor);
void StopPipeline();
void ReleasePipeline();
void DescribePipeline(obs_data_t* response);
void RegisterVendorRequests();
void UnregisterVendorRequests();
bool SourceAllowed(obs_source_t* source, std::uint64_t generation);
bool AuditScene();
void StopPublication(GuardStopReason reason = GuardStopReason::None);
void StopAllAudioSources();
void DescribeAudioSource(const AudioRoute& selected, obs_data_t* response);
void DescribeSelectedAudio(obs_data_t* response);
std::optional<AudioRoute> SelectedAudioRoute(const char* name, std::uint64_t generation, std::uint32_t owner_pid);
bool AudioSourceReady(const char* name, std::uint64_t generation, std::uint32_t owner_pid);
std::string AudioSourceError(const char* name, std::uint64_t generation);
void StartLocalControl();
void StopLocalControl();
std::optional<ProcessToken> ControllerIdentity();
bool ControllerAlive();
bool ControllerLost();
}
