#include <windows.h>
#include <string>
bool replacing = false;

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_APP + 2) {
        replacing = true; DestroyWindow(window); replacing = false;
        auto replacement = CreateWindowW(L"JKCaptureFixture", L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, 740, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        ShowWindow(replacement, SW_SHOWNOACTIVATE); SetTimer(replacement, 1, 16, nullptr); return 0;
    }
    if (message == WM_TIMER) { InvalidateRect(window, nullptr, FALSE); return 0; }
    if (message == WM_PAINT) {
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        RECT client{}; GetClientRect(window, &client);
        auto brush = CreateSolidBrush(RGB(16, 36, 64)); FillRect(dc, &client, brush); DeleteObject(brush);
        const int x = static_cast<int>((GetTickCount64() / 4) % 620);
        RECT block{x, 140, x + 90, 230}; brush = CreateSolidBrush(RGB(80, 220, 160)); FillRect(dc, &block, brush); DeleteObject(brush);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(240, 240, 240));
        const auto text = L"JKiller synthetic capture fixture " + std::to_wstring(GetTickCount64());
        TextOutW(dc, 20, 30, text.c_str(), static_cast<int>(text.size())); EndPaint(window, &paint); return 0;
    }
    if (message == WM_DESTROY) { if (!replacing) PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wparam, lparam);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    WNDCLASSW type{}; type.lpfnWndProc = WindowProcedure; type.hInstance = instance; type.lpszClassName = L"JKCaptureFixture";
    RegisterClassW(&type);
    HWND window = CreateWindowW(type.lpszClassName, L"JKiller synthetic fixture", WS_OVERLAPPEDWINDOW, 40, 40, 740, 480, nullptr, nullptr, instance, nullptr);
    // Capture needs a visible synthetic window; no desktop content is recorded.
    ShowWindow(window, SW_SHOWNOACTIVATE); SetTimer(window, 1, 16, nullptr);
    MSG message{}; while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    return 0;
}
