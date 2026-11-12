#include "InputPolicy.hpp"
#include <string>
namespace jk {
bool AllowsCreateInput(std::string_view kind) {
    // Only independently bound, application-owned sources may be created for audio.
    // Generic devices and hosts stay denied.
    return kind == "monitor_capture" || kind == "window_capture" || kind == "jk_window_capture" || kind == "jk_bound_capture" || kind == "color_source" || kind == "jk_process_audio" || kind == "jk_system_audio";
}
bool AllowsApplicationAudioImage(std::wstring_view image) {
    // Explicit selection is sufficient for any application, including playback hosts.
    return image.find_first_not_of(L" \t\r\n") != std::wstring_view::npos;
}
}
