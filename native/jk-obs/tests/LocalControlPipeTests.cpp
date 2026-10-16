#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "LocalControlPipe.hpp"
#include <iostream>
#include <atomic>
#include <stdexcept>

static void Require(bool value, const char* reason) { if (!value) throw std::runtime_error(reason); }
static HANDLE Connect(const std::wstring& name) {
    const auto path = L"\\\\.\\pipe\\" + name;
    for (int attempt = 0; attempt < 100; ++attempt) {
        HANDLE handle = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (handle != INVALID_HANDLE_VALUE) return handle;
        Sleep(10);
    }
    return INVALID_HANDLE_VALUE;
}
int main(int argc, char** argv) {
    if (argc == 2 && std::string(argv[1]) == "--owner") { Sleep(2500); return 0; }
    jk::LocalControlPipe server;
    const auto name = L"jkiller-test-" + std::to_wstring(GetCurrentProcessId());
    std::atomic<int> requests{0};
    HANDLE client = INVALID_HANDLE_VALUE;
    PROCESS_INFORMATION child{};
    try {
        auto owner = jk::ReadProcessToken(GetCurrentProcessId());
        Require(owner.has_value(), "This process identity must be available");
        const std::string mode = argc == 2 ? argv[1] : "roundtrip";
        if (mode == "wrong-owner") {
            wchar_t executable[32768]{}; GetModuleFileNameW(nullptr, executable, 32768);
            std::wstring command = L"\"" + std::wstring(executable) + L"\" --owner";
            STARTUPINFOW startup{}; startup.cb = sizeof(startup);
            Require(CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &child), "Owner fixture must launch");
            owner = jk::ReadProcessToken(child.dwProcessId);
        }
        Require(server.Start(name, *owner, [&requests](const std::string& message) { ++requests; return "reply:" + message; }), "Local pipe must start");
        client = Connect(name);
        Require(client != INVALID_HANDLE_VALUE, "Local pipe must accept a connection");
        DWORD bytes = 0;
        const std::uint32_t size = mode == "oversized" ? 1048577 : 4;
        WriteFile(client, &size, sizeof(size), &bytes, nullptr);
        if (mode == "roundtrip") {
            WriteFile(client, "ping", 4, &bytes, nullptr);
            std::uint32_t reply_size = 0;
            Require(ReadFile(client, &reply_size, 4, &bytes, nullptr) && reply_size == 10, "Reply framing must be exact");
            std::string reply(reply_size, '\0');
            Require(ReadFile(client, reply.data(), reply_size, &bytes, nullptr) && reply == "reply:ping", "Authorized caller must receive actual handler response");
            Require(requests == 1, "Exactly one request should execute");
        } else {
            char response{};
            Require(!ReadFile(client, &response, 1, &bytes, nullptr), "Rejected caller or frame must be disconnected");
            Require(requests == 0, "Rejected requests must never reach OBS");
        }
        CloseHandle(client); client = INVALID_HANDLE_VALUE;
        server.Stop();
        if (child.hProcess) { WaitForSingleObject(child.hProcess, 4000); CloseHandle(child.hThread); CloseHandle(child.hProcess); child = {}; }
        std::cout << "PASS local-control " << mode << '\n'; return 0;
    } catch (const std::exception& error) {
        if (client != INVALID_HANDLE_VALUE) CloseHandle(client);
        server.Stop();
        if (child.hProcess) { WaitForSingleObject(child.hProcess, 4000); CloseHandle(child.hThread); CloseHandle(child.hProcess); }
        std::cerr << "FAIL: " << error.what() << '\n'; return 1;
    }
}
