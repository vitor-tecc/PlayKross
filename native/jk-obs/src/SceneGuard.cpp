#include "SceneGuard.hpp"
namespace jk {
bool SceneGuard::Arm(std::unique_ptr<ProcessBinding> owner, std::optional<AudioRoute> selected, std::uint64_t generation) {
    std::lock_guard lock(mutex_);
    if (owner_) owner_->Seal();
    owner_.reset(); selected_.reset(); generation_ = 0;
    if (!owner || !owner->CanEmit() || !generation) return false;
    if (selected) {
        const bool owned_type = (selected->mode == AudioMode::Application && selected->type_id == "jk_process_audio") ||
            (selected->mode == AudioMode::SystemOutput && selected->type_id == "jk_system_audio");
        if (!owned_type || selected->source_name.empty() || selected->identity_key.empty() || selected->monitoring || selected->generation != generation) return false;
    }
    owner_ = std::move(owner); selected_ = std::move(selected); generation_ = generation;
    stop_reason_ = GuardStopReason::None;
    return true;
}
void SceneGuard::Disarm(GuardStopReason reason) {
    std::lock_guard lock(mutex_);
    if (stop_reason_ == GuardStopReason::None && reason != GuardStopReason::None) stop_reason_ = reason;
    if (owner_) owner_->Seal();
    owner_.reset(); selected_.reset(); generation_ = 0;
}
bool SceneGuard::Allows(const AudioRoute& route) const {
    std::lock_guard lock(mutex_);
    return owner_ && owner_->CanEmit() && selected_ && !route.monitoring &&
        route.mode == selected_->mode && route.type_id == selected_->type_id &&
        route.source_name == selected_->source_name && route.identity_key == selected_->identity_key && route.generation == generation_;
}
std::optional<AudioRoute> SceneGuard::SelectedAudio() const {
    std::lock_guard lock(mutex_);
    return owner_ && owner_->CanEmit() ? selected_ : std::nullopt;
}
bool SceneGuard::Active() const { std::lock_guard lock(mutex_); return owner_ && owner_->CanEmit(); }
GuardStopReason SceneGuard::StopReason() const { std::lock_guard lock(mutex_); return stop_reason_; }
const char* GuardStopReasonCode(GuardStopReason reason) {
    switch (reason) {
    case GuardStopReason::ControllerLost: return "controller_lost";
    case GuardStopReason::HeartbeatExpired: return "heartbeat_timeout";
    case GuardStopReason::VideoSourceLost: return "video_source_lost";
    case GuardStopReason::AudioGraphUnsafe: return "audio_graph_unsafe";
    case GuardStopReason::AudioMonitoringEnabled: return "audio_monitoring_enabled";
    case GuardStopReason::UnarmedOutput: return "unarmed_output";
    default: return "";
    }
}
}
