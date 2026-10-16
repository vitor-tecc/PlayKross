#include <windows.h>
#include <string>
#include <mmsystem.h>
#include <vector>
#include <cmath>
#include <thread>
#include <atomic>
#include <cstdint>
bool replacing = false;
std::uint64_t paint_count = 0;
bool timing_mode = false;
std::atomic<bool> clock_running{true}, paint_pending{false};
HWAVEOUT audio_output{};
std::uint32_t UnixMillisecondsLow() {
    FILETIME time{}; GetSystemTimePreciseAsFileTime(&time); ULARGE_INTEGER value{}; value.LowPart = time.dwLowDateTime; value.HighPart = time.dwHighDateTime;
    return static_cast<std::uint32_t>((value.QuadPart - 116444736000000000ULL) / 10000);
}

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_APP + 3) { KillTimer(window, 1); InvalidateRect(window, nullptr, FALSE); UpdateWindow(window); return 0; }
    if (message == WM_APP + 2) {
        replacing = true; DestroyWindow(window); replacing = false;
        auto replacement = CreateWindowW(L"JKCaptureFixture", L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, 740, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        ShowWindow(replacement, SW_SHOWNOACTIVATE); SetTimer(replacement, 1, 16, nullptr); return 0;
    }
    if (message == WM_TIMER) { InvalidateRect(window, nullptr, FALSE); return 0; }
    if (message == WM_APP + 4) { paint_pending = false; InvalidateRect(window, nullptr, FALSE); UpdateWindow(window); return 0; }
    if (message == WM_PAINT) {
        ++paint_count; SetPropW(window, L"JKFixturePaintCount", reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(paint_count)));
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        RECT client{}; GetClientRect(window, &client);
        if (timing_mode) {
            const auto timestamp = UnixMillisecondsLow();
            for (int row = 0; row < 12; ++row) {
                RECT band{0, row * client.bottom / 12, client.right, (row + 1) * client.bottom / 12};
                const int phase = static_cast<int>(paint_count % 256);
                auto color = CreateSolidBrush(RGB((phase * 3 + row * 17) % 256, (phase * 7 + row * 31) % 256, (phase * 11 + row * 13) % 256));
                FillRect(dc, &band, color); DeleteObject(color);
                const int moving = static_cast<int>((paint_count * 17 + row * 73) % (client.right + 150)) - 150;
                RECT tile{moving, band.top + 4, moving + 150, band.bottom - 4}; FillRect(dc, &tile, reinterpret_cast<HBRUSH>(GetStockObject(WHITE_BRUSH)));
            }
            // A fixed binary wall-clock strip bracketed by magenta sync blocks.
            // The acceptance receiver discovers its scaled position, never assumes a panel size.
            RECT left{16,64,32,96}, right{560,64,576,96}; auto sync = CreateSolidBrush(RGB(255,0,255)); FillRect(dc,&left,sync); FillRect(dc,&right,sync); DeleteObject(sync);
            for (int bit=0; bit<32; ++bit) { RECT cell{48+bit*16,64,64+bit*16,96}; FillRect(dc,&cell,reinterpret_cast<HBRUSH>(GetStockObject((timestamp & (1u << bit)) ? WHITE_BRUSH : BLACK_BRUSH))); }
            MMTIME position{}; position.wType=TIME_SAMPLES;
            const bool flash = audio_output && waveOutGetPosition(audio_output,&position,sizeof(position)) == MMSYSERR_NOERROR && position.wType == TIME_SAMPLES && position.u.sample % 48000 < 2400;
            RECT indicator{620,64,680,124}; FillRect(dc,&indicator,reinterpret_cast<HBRUSH>(GetStockObject(flash ? WHITE_BRUSH : BLACK_BRUSH)));
            SetBkMode(dc,TRANSPARENT); SetTextColor(dc,RGB(255,255,255)); const auto text=L"Synthetic high motion / frame " + std::to_wstring(paint_count); TextOutW(dc,16,16,text.c_str(),static_cast<int>(text.size()));
            EndPaint(window,&paint); return 0;
        }
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
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR command, int) {
    timing_mode = std::wstring(command).find(L"--timing") != std::wstring::npos;
    const double frequency = std::wstring(command).find(L"--prohibited-tone") != std::wstring::npos ? 1801 : 997;
    WAVEFORMATEX format{WAVE_FORMAT_PCM, 2, 48000, 192000, 4, 16, 0}; HWAVEOUT audio{};
    std::vector<short> pcm(96000);
    for (int frame = 0; frame < 48000; ++frame) { const short value = static_cast<short>((!timing_mode || frame < 2400 ? 700 : 0) * std::sin(2 * 3.141592653589793 * frequency * frame / 48000)); pcm[frame * 2] = value; pcm[frame * 2 + 1] = value; }
    WAVEHDR buffer{}; buffer.lpData = reinterpret_cast<char*>(pcm.data()); buffer.dwBufferLength = static_cast<DWORD>(pcm.size() * sizeof(short)); buffer.dwFlags = WHDR_BEGINLOOP | WHDR_ENDLOOP; buffer.dwLoops = 10000;
    if (waveOutOpen(&audio, WAVE_MAPPER, &format, 0, 0, CALLBACK_NULL) == MMSYSERR_NOERROR) { waveOutPrepareHeader(audio, &buffer, sizeof(buffer)); waveOutWrite(audio, &buffer, sizeof(buffer)); }
    audio_output = audio;
    WNDCLASSW type{}; type.lpfnWndProc = WindowProcedure; type.hInstance = instance; type.lpszClassName = L"JKCaptureFixture";
    RegisterClassW(&type);
    HWND window = CreateWindowW(type.lpszClassName, L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, timing_mode ? 1280 : 740, timing_mode ? 760 : 480, nullptr, nullptr, instance, nullptr);
    // Capture needs a visible synthetic window; no desktop content is recorded.
    ShowWindow(window, SW_SHOWNOACTIVATE);
    std::thread clock;
    if (timing_mode) clock = std::thread([window] {
        const auto timer = CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS);
        if (!timer) return; LARGE_INTEGER frequency{}, began{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&began);
        std::uint64_t tick=1;
        while (clock_running) {
            LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
            const double remaining = (static_cast<double>(tick) / 60) - static_cast<double>(now.QuadPart-began.QuadPart) / frequency.QuadPart;
            if (remaining > 0) { LARGE_INTEGER due{}; due.QuadPart=-static_cast<LONGLONG>(remaining*10000000); SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE); WaitForSingleObject(timer,100); }
            if (!paint_pending.exchange(true)) PostMessageW(window,WM_APP+4,0,0);
            ++tick;
            QueryPerformanceCounter(&now); const auto elapsed=static_cast<double>(now.QuadPart-began.QuadPart) / frequency.QuadPart;
            if (elapsed > static_cast<double>(tick)/60) tick=static_cast<std::uint64_t>(elapsed*60)+1;
        }
        CloseHandle(timer);
    }); else SetTimer(window, 1, 16, nullptr);
    MSG message{}; while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    clock_running=false; if (clock.joinable()) clock.join(); audio_output=nullptr;
    if (audio) { waveOutReset(audio); waveOutUnprepareHeader(audio, &buffer, sizeof(buffer)); waveOutClose(audio); }
    return 0;
}
