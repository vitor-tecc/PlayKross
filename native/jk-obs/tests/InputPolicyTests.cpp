#include "InputPolicy.hpp"
#include <iostream>
int main() {
    for (const char* kind : {"wasapi_input_capture","wasapi_output_capture","wasapi_process_output_capture","browser_source","unknown_source"}) {
        if (jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: arbitrary or unavailable audio input accepted: " << kind << '\n'; return 1; }
    }
    for (const char* kind : {"monitor_capture","window_capture","color_source","jk_process_audio","jk_system_audio"}) {
        if (!jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: required safe input denied\n"; return 1; }
    }
    for (const wchar_t* image : {L"C:\\Discord.exe", L"C:\\DiscordCanary.exe", L"C:\\JKiller.Windows.exe", L"C:\\PlayKross.exe", L"C:\\obs64.exe", L"C:\\chrome.exe", L"C:\\msedge.exe", L"C:\\msedgewebview2.exe", L"C:\\firefox.exe", L"C:\\brave.exe", L"C:\\opera.exe", L"C:\\chromium.exe", L"C:\\steamwebhelper.exe", L"C:\\explorer.exe", L"C:\\rundll32.exe", L"C:\\dllhost.exe", L"", L" \t"}) {
        if (jk::AllowsApplicationAudioImage(image)) { std::cerr << "FAIL: shared host or own playback audio accepted\n"; return 1; }
    }
    for (const wchar_t* image : {L"C:/Steam/steamapps/common/Geometry Dash/GeometryDash.exe", L"C:\\Tests\\CaptureFixture.exe", L"C:\\Games\\Game.exe", L"C:\\Apps\\MusicPlayer.exe", L"C:\\GeometryDash.exe", L"C:/Apps/MUSICPLAYER.EXE"}) {
        if (!jk::AllowsApplicationAudioImage(image)) { std::cerr << "FAIL: standalone application audio denied\n"; return 1; }
    }
    std::cout << "PASS StandaloneApplicationAllowedButSharedHostsDenied\n";
}
