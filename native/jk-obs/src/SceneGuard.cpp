#include "SceneGuard.hpp"
namespace jk {
bool SceneGuard::Arm(std::unique_ptr<ProcessBinding> owner, std::string source_name, std::uint64_t generation) {
    std::lock_guard lock(mutex_);
    owner_.reset(); generation_ = 0;
    if (!owner || !owner->CanEmit() || !generation) return false;
    owner_ = std::move(owner); source_name_ = std::move(source_name); generation_ = generation;
    return true;
}
void SceneGuard::Disarm() {
    std::lock_guard lock(mutex_);
    if (owner_) owner_->Seal();
    owner_.reset(); generation_ = 0;
}
bool SceneGuard::Allows(const AudioRoute& route) const {
    std::lock_guard lock(mutex_);
    return owner_ && owner_->CanEmit() && route.type_id == "jk_process_audio" && !source_name_.empty() &&
        route.source_name == source_name_ && route.generation == generation_ && !route.monitoring;
}
bool SceneGuard::Active() const { std::lock_guard lock(mutex_); return owner_ && owner_->CanEmit(); }
}
