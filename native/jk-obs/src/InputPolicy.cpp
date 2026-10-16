#include "InputPolicy.hpp"
#include <string>
#include <cwctype>
namespace jk {
bool AllowsCreateInput(std::string_view kind) {
    // Process audio is explicitly opt-in and experimental: Windows includes future
    // children. System/device/browser sources remain prohibited.
    return kind == "monitor_capture" || kind == "window_capture" || kind == "jk_window_capture" || kind == "jk_bound_capture" || kind == "color_source" || kind == "jk_process_audio";
}
bool AllowsExperimentalAudioImage(std::wstring_view image) {
    std::wstring normalized(image);
    for (auto& ch : normalized) ch = ch == L'/' ? L'\\' : static_cast<wchar_t>(std::towlower(ch));
    return normalized.ends_with(L"\\steamapps\\common\\geometry dash\\geometrydash.exe") || normalized.ends_with(L"\\capturefixture.exe");
}
}
