#include "InputPolicy.hpp"
#include <string>
#include <cwctype>
namespace jk {
bool AllowsCreateInput(std::string_view kind) {
    // Only independently bound, application-owned sources may be created for audio.
    // Generic devices and hosts stay denied.
    return kind == "monitor_capture" || kind == "window_capture" || kind == "jk_window_capture" || kind == "jk_bound_capture" || kind == "color_source" || kind == "jk_process_audio" || kind == "jk_system_audio";
}
bool AllowsApplicationAudioImage(std::wstring_view image) {
    if (image.empty() || image.find_first_not_of(L" \t\r\n") == std::wstring_view::npos) return false;
    std::wstring name(image.substr(image.find_last_of(L"/\\") == std::wstring_view::npos ? 0 : image.find_last_of(L"/\\") + 1));
    for (auto& ch : name) ch = static_cast<wchar_t>(std::towlower(ch));
    // Keep in parity with managed AudioPolicy.EvaluateApplication. Native binding
    // and owner-tree validation remain independent requirements after eligibility.
    return name.find(L"discord") == std::wstring::npos && name.find(L"jkiller") == std::wstring::npos && name.find(L"playkross") == std::wstring::npos &&
        name != L"obs64.exe" && name != L"chrome.exe" && name != L"msedge.exe" && name != L"msedgewebview2.exe" &&
        name != L"firefox.exe" && name != L"brave.exe" && name != L"opera.exe" && name != L"chromium.exe" &&
        name != L"steamwebhelper.exe" && name != L"explorer.exe" && name != L"rundll32.exe" && name != L"dllhost.exe";
}
}
