#include "InputPolicy.hpp"
namespace jk {
bool AllowsCreateInput(std::string_view kind) {
    return kind == "monitor_capture" || kind == "window_capture" || kind == "jk_window_capture" || kind == "jk_bound_capture" || kind == "jk_process_audio" || kind == "color_source";
}
}
