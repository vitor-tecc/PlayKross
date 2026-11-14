#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "ProcessBinding.hpp"
#include <functional>
#include <iostream>
#include <map>
#include <stdexcept>

static void Require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
struct FakeLifetime final : jk::IProcessLifetime {
    jk::ProcessToken token{42, 1000, L"C:\\game.exe"};
    bool alive = true;
    jk::ProcessToken Identity() const override { return token; }
    bool Alive() const override { return alive; }
};
struct FakeProbe final : jk::IProcessProbe {
    std::shared_ptr<FakeLifetime> process = std::make_shared<FakeLifetime>();
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return process; }
};
static void RejectsPidReuse() {
    FakeProbe probe;
    auto requested = probe.process->token;
    probe.process->token.creation_filetime++;
    auto result = jk::ProcessBinding::Open(requested, probe);
    Require(!result.binding, "Reused PID must not bind to replacement");
}
static void SealedBindingNeverReopens() {
    FakeProbe probe;
    auto result = jk::ProcessBinding::Open(probe.process->token, probe);
    Require(result.binding && result.binding->CanEmit(), "Original live process should bind");
    result.binding->Seal();
    Require(!result.binding->CanEmit(), "Sealed binding must stay silent");
}
static void ExitDuringActivationCannotEmit() {
    FakeProbe probe;
    auto result = jk::ProcessBinding::Open(probe.process->token, probe);
    probe.process->alive = false;
    Require(!result.binding->CanEmit(), "Exited original must not emit");
    probe.process->alive = true;
    Require(!result.binding->CanEmit(), "Observed exit must seal permanently");
}
static void AccessDeniedDoesNotFallback() {
    FakeProbe probe;
    probe.process.reset();
    auto result = jk::ProcessBinding::Open({42, 1000, L"C:\\game.exe"}, probe);
    Require(!result.binding && !result.error.empty(), "Denied process must fail closed");
}
static void RealProcessExitCannotRebind() {
    wchar_t executable[32768]{};
    GetModuleFileNameW(nullptr, executable, 32768);
    std::wstring command = L"\"" + std::wstring(executable) + L"\" --child";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION child{};
    Require(CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &child), "Child test process must start");
    auto token = jk::ReadProcessToken(child.dwProcessId);
    auto result = token ? jk::ProcessBinding::Open(*token) : jk::BindingResult{nullptr, "No identity"};
    const bool bound = result.binding && result.binding->CanEmit();
    WaitForSingleObject(child.hProcess, 5000);
    CloseHandle(child.hThread); CloseHandle(child.hProcess);
    Require(bound, "Real child identity must bind");
    Require(!result.binding->CanEmit(), "Exited child must become silent");
    Require(!jk::ProcessBinding::Open(*token).binding, "Original identity cannot be reacquired after exit");
}
int main(int argc, char** argv) {
    if (argc == 2 && std::string(argv[1]) == "--child") { Sleep(500); return 0; }
    std::map<std::string, std::function<void()>> cases{{"RejectsPidReuse", RejectsPidReuse}, {"SealedBindingNeverReopens", SealedBindingNeverReopens}, {"ExitDuringActivationCannotEmit", ExitDuringActivationCannotEmit}, {"AccessDeniedDoesNotFallback", AccessDeniedDoesNotFallback}, {"RealProcessExitCannotRebind", RealProcessExitCannotRebind}};
    try { cases.at(argc == 2 ? argv[1] : "")( ); std::cout << "PASS " << argv[1] << '\n'; return 0; }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
