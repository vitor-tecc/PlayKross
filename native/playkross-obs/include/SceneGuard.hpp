#pragma once
#include "ProcessBinding.hpp"
#include <mutex>

namespace jk {
enum class AudioMode { Off, Application, SystemOutput };
enum class GuardStopReason { None, ControllerLost, HeartbeatExpired, VideoSourceLost, AudioGraphUnsafe, AudioMonitoringEnabled, UnarmedOutput };
const char* GuardStopReasonCode(GuardStopReason reason);
struct AudioRoute {
    std::string type_id;
    std::string source_name;
    std::uint64_t generation;
    bool monitoring;
    AudioMode mode;
    std::string identity_key;
};
class SceneGuard {
public:
    bool Arm(std::unique_ptr<ProcessBinding> owner, std::optional<AudioRoute> selected, std::uint64_t generation);
    void Disarm(GuardStopReason reason = GuardStopReason::None);
    GuardStopReason StopReason() const;
    bool Allows(const AudioRoute& route) const;
    std::optional<AudioRoute> SelectedAudio() const;
    bool Active() const;
private:
    std::unique_ptr<ProcessBinding> owner_;
    std::optional<AudioRoute> selected_;
    std::uint64_t generation_ = 0;
    GuardStopReason stop_reason_ = GuardStopReason::None;
    mutable std::mutex mutex_;
};
}
