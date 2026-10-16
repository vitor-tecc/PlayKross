#include "VideoLease.hpp"
namespace jk {
bool VideoLease::CanRender(bool original_process_alive, bool original_item_active, bool handle_valid, std::uint32_t observed_pid) {
    if (!original_process_alive || !original_item_active || !handle_valid || observed_pid != pid_) sealed_ = true;
    return !sealed_;
}
}
