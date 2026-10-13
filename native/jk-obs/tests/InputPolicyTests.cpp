#include "InputPolicy.hpp"
#include <iostream>
int main() {
    for (const char* kind : {"wasapi_input_capture","wasapi_output_capture","wasapi_process_output_capture","browser_source","unknown_source"}) {
        if (jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: unsafe input accepted: " << kind << '\n'; return 1; }
    }
    for (const char* kind : {"monitor_capture","window_capture","jk_process_audio","color_source"}) {
        if (!jk::AllowsCreateInput(kind)) { std::cerr << "FAIL: required safe input denied\n"; return 1; }
    }
    std::cout << "PASS: only video and bound process-audio creation permitted\n";
}
