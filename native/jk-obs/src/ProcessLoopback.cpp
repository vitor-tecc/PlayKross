#include "ProcessLoopback.hpp"
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl.h>
#include <condition_variable>
#include <future>
#include <sstream>
#include <thread>
#include <cstring>
namespace jk {
namespace {
using Microsoft::WRL::ComPtr;
using UniqueHandle = std::unique_ptr<void, decltype(&CloseHandle)>;
class Activation final : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, Microsoft::WRL::FtmBase, IActivateAudioInterfaceCompletionHandler> {
public:
    UniqueHandle completed{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
    ComPtr<IAudioClient> client;
    std::atomic<HRESULT> result{E_PENDING};
    HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override {
        HRESULT activation = E_FAIL;
        ComPtr<IUnknown> unknown;
        HRESULT code = operation->GetActivateResult(&activation, &unknown);
        if (SUCCEEDED(code)) code = activation;
        if (SUCCEEDED(code)) code = unknown.As(&client);
        result.store(code, std::memory_order_release);
        SetEvent(completed.get());
        return S_OK;
    }
};
std::string Failure(HRESULT code) { std::ostringstream text; text << "Application audio API failed (0x" << std::hex << static_cast<unsigned long>(code) << "). Audio remains disabled."; return text.str(); }
void Check(HRESULT code) { if (FAILED(code)) throw std::runtime_error(Failure(code)); }
std::uint64_t NowNs() {
    LARGE_INTEGER value{}, frequency{}; QueryPerformanceCounter(&value); QueryPerformanceFrequency(&frequency);
    return static_cast<std::uint64_t>((static_cast<long double>(value.QuadPart) * 1000000000.0L) / frequency.QuadPart);
}
}
struct ProcessLoopback::Impl {
    ProcessBinding& binding;
    AudioSink& sink;
    UniqueHandle stop{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
    std::thread worker;
    std::thread emitter;
    std::atomic_bool running{false};
    std::mutex mutex;
    std::condition_variable ready;
    PcmQueue queue;
    std::string error;
    Impl(ProcessBinding& process, AudioSink& output) : binding(process), sink(output) { }
    void SetError(std::string text) { std::lock_guard lock(mutex); error = std::move(text); }
    void Emit() {
        while (WaitForSingleObject(stop.get(), 0) == WAIT_TIMEOUT) {
            std::optional<PcmBlock> block;
            {
                std::unique_lock lock(mutex);
                ready.wait_for(lock, std::chrono::milliseconds(50), [this] { return queue.DurationMs() > 0 || WaitForSingleObject(stop.get(), 0) != WAIT_TIMEOUT; });
                block = queue.Pop();
            }
            if (block && !DeliverAudio(binding, sink, *block)) { SetEvent(stop.get()); break; }
        }
    }
    void Run(std::promise<CaptureResult> started) {
        bool signaled = false;
        const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        try {
            Check(initialized);
            if (!stop || !binding.CanEmit() || !binding.WaitHandle()) throw std::runtime_error("Selected application is unavailable.");
            auto activation = Microsoft::WRL::Make<Activation>();
            if (!activation || !activation->completed) throw std::runtime_error("Unable to initialize application audio activation.");
            AUDIOCLIENT_ACTIVATION_PARAMS params{};
            params.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
            params.ProcessLoopbackParams.TargetProcessId = binding.Identity().pid;
            params.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
            PROPVARIANT variant{}; variant.vt = VT_BLOB;
            variant.blob.cbSize = sizeof(params); variant.blob.pBlobData = reinterpret_cast<BYTE*>(&params);
            ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
            Check(ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, __uuidof(IAudioClient), &variant, activation.Get(), &operation));
            HANDLE activationWait[]{stop.get(), reinterpret_cast<HANDLE>(binding.WaitHandle()), activation->completed.get()};
            if (WaitForMultipleObjects(3, activationWait, FALSE, 5000) != WAIT_OBJECT_0 + 2 || !binding.CanEmit()) throw std::runtime_error("Application audio activation cancelled, timed out, or source exited.");
            Check(activation->result.load(std::memory_order_acquire));
            auto client = activation->client;
            WAVEFORMATEX format{WAVE_FORMAT_IEEE_FLOAT, 2, 48000, 384000, 8, 32, 0};
            Check(client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, 0, 0, &format, nullptr));
            UniqueHandle samplesReady{CreateEventW(nullptr, FALSE, FALSE, nullptr), &CloseHandle};
            if (!samplesReady) throw std::runtime_error("Unable to create application audio event.");
            Check(client->SetEventHandle(samplesReady.get()));
            ComPtr<IAudioCaptureClient> capture;
            Check(client->GetService(IID_PPV_ARGS(&capture)));
            if (!binding.CanEmit()) throw std::runtime_error("Selected application exited during audio setup.");
            Check(client->Start());
            running.store(true);
            emitter = std::thread([this] { Emit(); });
            started.set_value({true, ""}); signaled = true;
            HANDLE waits[]{stop.get(), reinterpret_cast<HANDLE>(binding.WaitHandle()), samplesReady.get()};
            while (binding.CanEmit()) {
                const DWORD event = WaitForMultipleObjects(3, waits, FALSE, 1000);
                if (event == WAIT_OBJECT_0 || event == WAIT_OBJECT_0 + 1) break;
                if (event == WAIT_TIMEOUT) continue;
                if (event != WAIT_OBJECT_0 + 2) throw std::runtime_error("Application audio event failed.");
                UINT32 frames = 0;
                Check(capture->GetNextPacketSize(&frames));
                while (frames && binding.CanEmit()) {
                    BYTE* data = nullptr; DWORD flags = 0; UINT64 devicePosition = 0, qpcPosition = 0;
                    Check(capture->GetBuffer(&data, &frames, &flags, &devicePosition, &qpcPosition));
                    PcmBlock block{std::vector<float>(static_cast<std::size_t>(frames) * 2), 2, 48000, (flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) || !qpcPosition ? NowNs() : qpcPosition * 100};
                    if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && data) std::memcpy(block.samples.data(), data, block.samples.size() * sizeof(float));
                    Check(capture->ReleaseBuffer(frames));
                    if (binding.CanEmit()) { std::lock_guard lock(mutex); queue.Push(std::move(block)); }
                    ready.notify_one();
                    Check(capture->GetNextPacketSize(&frames));
                }
            }
            client->Stop();
            if (!binding.CanEmit()) SetError("Selected application exited. Audio was muted.");
        } catch (const std::exception& failure) {
            SetError(failure.what());
            binding.Seal();
            if (!signaled) { started.set_value({false, failure.what()}); signaled = true; }
        }
        running.store(false);
        SetEvent(stop.get()); ready.notify_all();
        if (emitter.joinable()) emitter.join();
        { std::lock_guard lock(mutex); queue.Clear(); }
        if (SUCCEEDED(initialized)) CoUninitialize();
    }
};
ProcessLoopback::ProcessLoopback() = default;
ProcessLoopback::~ProcessLoopback() { Stop(); }
CaptureResult ProcessLoopback::Start(ProcessBinding& binding, AudioSink& sink) {
    Stop();
    impl_ = std::make_unique<Impl>(binding, sink);
    std::promise<CaptureResult> started;
    auto result = started.get_future();
    impl_->worker = std::thread([state = impl_.get(), promise = std::move(started)]() mutable { state->Run(std::move(promise)); });
    if (result.wait_for(std::chrono::seconds(6)) != std::future_status::ready) { Stop(); return {false, "Application audio initialization timed out."}; }
    return result.get();
}
void ProcessLoopback::Stop() {
    if (!impl_) return;
    SetEvent(impl_->stop.get()); impl_->ready.notify_all();
    if (impl_->worker.joinable()) impl_->worker.join();
}
bool ProcessLoopback::Running() const { return impl_ && impl_->running.load(); }
std::string ProcessLoopback::Error() const { if (!impl_) return ""; std::lock_guard lock(impl_->mutex); return impl_->error; }
}
