#include "InputPolicy.hpp"
namespace jk {
bool AllowsCreateInput(std::string_view kind) {
    // INCLUDE_TARGET_PROCESS_TREE also admits future children. A filename allowlist
    // or polling cannot enforce exclusion; deny this input until confinement exists.
    return kind == "monitor_capture" || kind == "window_capture" || kind == "jk_window_capture" || kind == "jk_bound_capture" || kind == "color_source";
}
}
