#include "InputPolicy.hpp"
#include <iostream>
int main() {
    for (const char* kind : {"wasapi_input_capture","wasapi_output_capture","wasapi_process_output_capture","browser_source","unknown_source"}) {
        if (jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: unsafe input accepted: " << kind << '\n'; return 1; }
    }
    for (const char* kind : {"monitor_capture","window_capture","color_source","jk_process_audio"}) {
        if (!jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: required safe input denied\n"; return 1; }
    }
    for (const wchar_t* image : {L"C:\\Discord.exe", L"C:\\PlayKross.exe", L"C:\\chrome.exe", L"C:\\GeometryDash.exe", L"C:\\Game.exe", L""}) {
        if (jk::AllowsExperimentalAudioImage(image)) { std::cerr << "FAIL: unsupported audio image accepted\n"; return 1; }
    }
    if (!jk::AllowsExperimentalAudioImage(L"C:/Steam/steamapps/common/Geometry Dash/GeometryDash.exe") ||
        !jk::AllowsExperimentalAudioImage(L"C:\\Tests\\CaptureFixture.exe")) { std::cerr << "FAIL: supported experimental audio denied\n"; return 1; }
    std::cout << "PASS: only video and bound process-audio creation permitted\n";
}
