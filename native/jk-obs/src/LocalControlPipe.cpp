#include "LocalControlPipe.hpp"
#include <windows.h>
#include <sddl.h>
#include <future>
#include <thread>
#include <algorithm>
namespace jk {
namespace {
using UniqueHandle = std::unique_ptr<void, decltype(&CloseHandle)>;
}
struct LocalControlPipe::Impl {
    std::wstring name;
    std::unique_ptr<ProcessBinding> owner;
    Handler handler;
    UniqueHandle stop{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
    std::thread thread;
    Impl(std::wstring value, std::unique_ptr<ProcessBinding> binding, Handler process) : name(std::move(value)), owner(std::move(binding)), handler(std::move(process)) { }
    bool Await(HANDLE pipe, OVERLAPPED& operation, bool completed, DWORD& bytes, DWORD timeout) {
        if (completed) return true;
        if (GetLastError() != ERROR_IO_PENDING) return false;
        HANDLE waits[]{stop.get(), reinterpret_cast<HANDLE>(owner->WaitHandle()), operation.hEvent};
        if (WaitForMultipleObjects(3, waits, FALSE, timeout) != WAIT_OBJECT_0 + 2) {
            CancelIoEx(pipe, &operation);
            GetOverlappedResult(pipe, &operation, &bytes, TRUE); // Stack OVERLAPPED stays alive until cancellation completes.
            return false;
        }
        return GetOverlappedResult(pipe, &operation, &bytes, FALSE) != FALSE;
    }
    bool Transfer(HANDLE pipe, void* data, DWORD length, bool write) {
        auto* cursor = static_cast<std::uint8_t*>(data);
        DWORD done = 0;
        while (done < length && owner->CanEmit()) {
            UniqueHandle event{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
            if (!event) return false;
            OVERLAPPED operation{}; operation.hEvent = event.get();
            DWORD transferred = 0;
            const bool completed = write ? WriteFile(pipe, cursor + done, length - done, &transferred, &operation) != FALSE : ReadFile(pipe, cursor + done, length - done, &transferred, &operation) != FALSE;
            if (!Await(pipe, operation, completed, transferred, 10000) || !transferred) return false;
            done += transferred;
        }
        return done == length;
    }
    void Run(std::promise<bool> started) {
        PSECURITY_DESCRIPTOR descriptor = nullptr;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:P(A;;GA;;;SY)(A;;GA;;;OW)", SDDL_REVISION_1, &descriptor, nullptr)) { started.set_value(false); return; }
        std::unique_ptr<void, decltype(&LocalFree)> security(descriptor, &LocalFree);
        SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
        const auto path = L"\\\\.\\pipe\\" + name;
        UniqueHandle pipe(CreateNamedPipeW(path.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, 65536, 65536, 1000, &attributes), &CloseHandle);
        if (pipe.get() == INVALID_HANDLE_VALUE) { pipe.release(); started.set_value(false); return; }
        started.set_value(true);
        while (owner->CanEmit() && WaitForSingleObject(stop.get(), 0) == WAIT_TIMEOUT) {
            UniqueHandle event{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
            if (!event) break;
            OVERLAPPED operation{}; operation.hEvent = event.get();
            DWORD bytes = 0;
            const bool connected = ConnectNamedPipe(pipe.get(), &operation) != FALSE;
            const auto code = connected ? ERROR_SUCCESS : GetLastError();
            bool ready = connected || code == ERROR_PIPE_CONNECTED;
            if (!ready) { SetLastError(code); ready = Await(pipe.get(), operation, false, bytes, INFINITE); }
            if (!ready) break;
            ULONG caller = 0;
            if (GetNamedPipeClientProcessId(pipe.get(), &caller) && caller == owner->Identity().pid && owner->CanEmit()) {
                while (owner->CanEmit() && WaitForSingleObject(stop.get(), 0) == WAIT_TIMEOUT) {
                    std::uint32_t size = 0;
                    if (!Transfer(pipe.get(), &size, sizeof(size), false) || !size || size > 1048576) break;
                    std::string request(size, '\0');
                    if (!Transfer(pipe.get(), request.data(), size, false)) break;
                    std::string response;
                    try { response = handler(request); } catch (...) { break; }
                    if (response.empty() || response.size() > 1048576) break;
                    size = static_cast<std::uint32_t>(response.size());
                    if (!Transfer(pipe.get(), &size, sizeof(size), true) || !Transfer(pipe.get(), response.data(), size, true)) break;
                }
            }
            DisconnectNamedPipe(pipe.get());
        }
    }
};
LocalControlPipe::LocalControlPipe() = default;
LocalControlPipe::~LocalControlPipe() { Stop(); }
bool LocalControlPipe::Start(const std::wstring& name, const ProcessToken& owner, Handler handler) {
    Stop();
    if (name.empty() || name.size() > 128 || !std::all_of(name.begin(), name.end(), [](wchar_t ch) { return (ch >= L'a' && ch <= L'z') || (ch >= L'A' && ch <= L'Z') || (ch >= L'0' && ch <= L'9') || ch == L'-'; })) return false;
    auto binding = ProcessBinding::Open(owner);
    if (!binding.binding || !binding.binding->WaitHandle()) return false;
    impl_ = std::make_unique<Impl>(name, std::move(binding.binding), std::move(handler));
    std::promise<bool> started; auto ready = started.get_future();
    impl_->thread = std::thread([state = impl_.get(), promise = std::move(started)]() mutable { state->Run(std::move(promise)); });
    if (ready.wait_for(std::chrono::seconds(2)) != std::future_status::ready) { Stop(); return false; }
    return ready.get();
}
void LocalControlPipe::Stop() {
    if (!impl_) return;
    SetEvent(impl_->stop.get());
    if (impl_->thread.joinable()) impl_->thread.join();
}
}
