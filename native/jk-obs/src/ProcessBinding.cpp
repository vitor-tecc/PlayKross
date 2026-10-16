#include "ProcessBinding.hpp"
#include <windows.h>
#include <vector>
namespace jk {
namespace {
class WindowsLifetime final : public IProcessLifetime {
public:
    WindowsLifetime(HANDLE handle, ProcessToken token) : handle_(handle), token_(std::move(token)) { }
    ~WindowsLifetime() override { CloseHandle(handle_); }
    ProcessToken Identity() const override { return token_; }
    bool Alive() const override { return WaitForSingleObject(handle_, 0) == WAIT_TIMEOUT; }
    std::uintptr_t WaitHandle() const override { return reinterpret_cast<std::uintptr_t>(handle_); }
private:
    HANDLE handle_;
    ProcessToken token_;
};
class WindowsProbe final : public IProcessProbe {
public:
    std::shared_ptr<IProcessLifetime> Open(std::uint32_t pid) override {
        std::unique_ptr<void, decltype(&CloseHandle)> handle(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid), &CloseHandle);
        if (!handle) return nullptr;
        FILETIME created{}, exited{}, kernel{}, user{};
        std::vector<wchar_t> image(32768);
        DWORD size = static_cast<DWORD>(image.size());
        if (WaitForSingleObject(handle.get(), 0) != WAIT_TIMEOUT || !GetProcessTimes(handle.get(), &created, &exited, &kernel, &user) || !QueryFullProcessImageNameW(handle.get(), 0, image.data(), &size) || WaitForSingleObject(handle.get(), 0) != WAIT_TIMEOUT) {
            return nullptr;
        }
        const std::uint64_t creation = (static_cast<std::uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime;
        auto lifetime = std::make_shared<WindowsLifetime>(handle.get(), ProcessToken{pid, creation, std::wstring(image.data(), size)});
        handle.release();
        return lifetime;
    }
};
bool SameIdentity(const ProcessToken& selected, const ProcessToken& actual) {
    return selected.pid == actual.pid && selected.creation_filetime == actual.creation_filetime &&
        CompareStringOrdinal(selected.image_path.c_str(), -1, actual.image_path.c_str(), -1, TRUE) == CSTR_EQUAL;
}
}
BindingResult ProcessBinding::Open(const ProcessToken& token, IProcessProbe& probe) {
    if (!token.pid || !token.creation_filetime || token.image_path.empty()) return {nullptr, "Incomplete application identity."};
    auto lifetime = probe.Open(token.pid);
    if (!lifetime) return {nullptr, "Application access denied or process unavailable."};
    if (!lifetime->Alive() || !SameIdentity(token, lifetime->Identity()) || !lifetime->Alive()) return {nullptr, "The selected application has exited or been replaced."};
    return {std::unique_ptr<ProcessBinding>(new ProcessBinding(token, std::move(lifetime))), ""};
}
BindingResult ProcessBinding::Open(const ProcessToken& token) { WindowsProbe probe; return Open(token, probe); }
bool ProcessBinding::CanEmit() const {
    if (sealed_.load()) return false;
    if (!lifetime_->Alive()) { sealed_.store(true); return false; }
    return !sealed_.load();
}
void ProcessBinding::Seal() { sealed_.store(true); }
std::optional<ProcessToken> ReadProcessToken(std::uint32_t pid) {
    WindowsProbe probe;
    auto lifetime = probe.Open(pid);
    if (!lifetime || !lifetime->Alive()) return std::nullopt;
    return lifetime->Identity();
}
}
