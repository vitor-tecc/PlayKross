#pragma once
#include <cstdint>
namespace jk {
// The capture item is created once. A closed item/window seals the selection.
class VideoLease {
public:
    explicit VideoLease(std::uint32_t pid) : pid_(pid) {}
    bool CanRender(bool original_process_alive, bool original_item_active, bool handle_valid, std::uint32_t observed_pid);
    void Seal() { sealed_ = true; }
private:
    std::uint32_t pid_;
    bool sealed_ = false;
};
}
