#pragma once
#include <obs-module.h>
#include "SceneGuard.hpp"
#include "ProcessLoopback.hpp"

namespace jk {
extern SceneGuard module_guard;
extern std::atomic<std::uint64_t> last_heartbeat_ns;
void RegisterAudioSource();
void RegisterVendorRequests();
void UnregisterVendorRequests();
bool SourceAllowed(obs_source_t* source, std::uint64_t generation);
bool AuditScene();
void StopPublication();
void StopAllAudioSources();
void DescribeAudioSource(const char* name, obs_data_t* response);
bool AudioSourceReady(const char* name, std::uint64_t generation, std::uint32_t owner_pid);
void StartLocalControl();
void StopLocalControl();
std::optional<ProcessToken> ControllerIdentity();
}
