#include <windows.h>
#include <string>
#include <mmsystem.h>
#include <vector>
#include <cmath>
bool replacing = false;
std::uint64_t paint_count = 0;

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_APP + 3) { KillTimer(window, 1); InvalidateRect(window, nullptr, FALSE); UpdateWindow(window); return 0; }
    if (message == WM_APP + 2) {
        replacing = true; DestroyWindow(window); replacing = false;
        auto replacement = CreateWindowW(L"JKCaptureFixture", L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, 740, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        ShowWindow(replacement, SW_SHOWNOACTIVATE); SetTimer(replacement, 1, 16, nullptr); return 0;
    }
    if (message == WM_TIMER) { InvalidateRect(window, nullptr, FALSE); return 0; }
    if (message == WM_PAINT) {
        ++paint_count; SetPropW(window, L"JKFixturePaintCount", reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(paint_count)));
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        RECT client{}; GetClientRect(window, &client);
        auto brush = CreateSolidBrush(RGB(16, 36, 64)); FillRect(dc, &client, brush); DeleteObject(brush);
        const int x = static_cast<int>((GetTickCount64() / 4) % 620);
        RECT block{x, 140, x + 90, 230}; brush = CreateSolidBrush(RGB(80, 220, 160)); FillRect(dc, &block, brush); DeleteObject(brush);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(240, 240, 240));
        const auto text = L"JKiller synthetic capture fixture " + std::to_wstring(GetTickCount64());
        TextOutW(dc, 20, 30, text.c_str(), static_cast<int>(text.size())); EndPaint(window, &paint); return 0;
    }
    if (message == WM_DESTROY) { RemovePropW(window, L"JKFixturePaintCount"); if (!replacing) PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wparam, lparam);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    WAVEFORMATEX format{WAVE_FORMAT_PCM, 2, 48000, 192000, 4, 16, 0}; HWAVEOUT audio{};
    std::vector<short> pcm(96000);
    for (int frame = 0; frame < 48000; ++frame) { const short value = static_cast<short>(700 * std::sin(2 * 3.141592653589793 * 997 * frame / 48000)); pcm[frame * 2] = value; pcm[frame * 2 + 1] = value; }
    WAVEHDR buffer{}; buffer.lpData = reinterpret_cast<char*>(pcm.data()); buffer.dwBufferLength = static_cast<DWORD>(pcm.size() * sizeof(short)); buffer.dwFlags = WHDR_BEGINLOOP | WHDR_ENDLOOP; buffer.dwLoops = 10000;
    if (waveOutOpen(&audio, WAVE_MAPPER, &format, 0, 0, CALLBACK_NULL) == MMSYSERR_NOERROR) { waveOutPrepareHeader(audio, &buffer, sizeof(buffer)); waveOutWrite(audio, &buffer, sizeof(buffer)); }
    WNDCLASSW type{}; type.lpfnWndProc = WindowProcedure; type.hInstance = instance; type.lpszClassName = L"JKCaptureFixture";
    RegisterClassW(&type);
    HWND window = CreateWindowW(type.lpszClassName, L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, 740, 480, nullptr, nullptr, instance, nullptr);
    // Capture needs a visible synthetic window; no desktop content is recorded.
    ShowWindow(window, SW_SHOWNOACTIVATE); SetTimer(window, 1, 16, nullptr);
    MSG message{}; while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    if (audio) { waveOutReset(audio); waveOutUnprepareHeader(audio, &buffer, sizeof(buffer)); waveOutClose(audio); }
    return 0;
}
