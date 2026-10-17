#include "EndpointCaptureClient.hpp"
#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include <cstring>
#include <sstream>

namespace jk {
namespace {
using Microsoft::WRL::ComPtr;
void Check(HRESULT code) {
    if (FAILED(code)) {
        std::ostringstream error;
        error << "Selected output API failed (0x" << std::hex << static_cast<unsigned long>(code) << "). Audio remains disabled.";
        throw std::runtime_error(error.str());
    }
}
std::uint64_t NowNs() {
    LARGE_INTEGER value{}, frequency{};
    QueryPerformanceCounter(&value); QueryPerformanceFrequency(&frequency);
    return static_cast<std::uint64_t>(static_cast<long double>(value.QuadPart) * 1000000000.0L / frequency.QuadPart);
}
class WindowsEndpointCaptureClient final : public IEndpointCaptureClient {
    ComPtr<IAudioClient> client_;
    ComPtr<IAudioCaptureClient> capture_;
    std::unique_ptr<void, decltype(&CloseHandle)> samples_{nullptr, &CloseHandle};
    bool initialized_ = false;
public:
    ~WindowsEndpointCaptureClient() override { Stop(); }
    CaptureResult Open(std::wstring_view endpointId) override {
        Stop();
        try {
            if (endpointId.empty()) throw std::runtime_error("Select a concrete output endpoint.");
            Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
            initialized_ = true;
            ComPtr<IMMDeviceEnumerator> enumerator;
            Check(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator)));
            ComPtr<IMMDevice> device;
            const std::wstring id(endpointId);
            Check(enumerator->GetDevice(id.c_str(), &device));
            DWORD state = 0;
            Check(device->GetState(&state));
            if (state != DEVICE_STATE_ACTIVE) throw std::runtime_error("Selected output endpoint is not active.");
            ComPtr<IMMEndpoint> endpoint;
            Check(device.As(&endpoint));
            EDataFlow flow{};
            Check(endpoint->GetDataFlow(&flow));
            if (flow != eRender) throw std::runtime_error("Selected endpoint is not a render output.");
            Check(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(client_.GetAddressOf())));
            WAVEFORMATEX format{WAVE_FORMAT_IEEE_FLOAT, 2, 48000, 384000, 8, 32, 0};
            Check(client_->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                0, 0, &format, nullptr));
            samples_.reset(CreateEventW(nullptr, FALSE, FALSE, nullptr));
            if (!samples_) throw std::runtime_error("Unable to create output audio event.");
            Check(client_->SetEventHandle(samples_.get()));
            Check(client_->GetService(IID_PPV_ARGS(&capture_)));
            Check(client_->Start());
            return {true, ""};
        } catch (const std::exception& error) {
            const std::string text = error.what();
            Stop();
            return {false, text};
        }
    }
    EndpointRead Read(uintptr_t stopEvent, uintptr_t controllerEvent) override {
        try {
            // Drain all queued packets before waiting for the next sample event.
            UINT32 frames = 0;
            Check(capture_->GetNextPacketSize(&frames));
            if (!frames) {
                if (!controllerEvent) throw std::runtime_error("Controller lifetime event is unavailable.");
                HANDLE waits[]{reinterpret_cast<HANDLE>(stopEvent), reinterpret_cast<HANDLE>(controllerEvent), samples_.get()};
                const auto event = WaitForMultipleObjects(3, waits, FALSE, 100);
                if (event == WAIT_OBJECT_0 || event == WAIT_OBJECT_0 + 1) return {EndpointRead::Status::Stopped, {}, ""};
                if (event == WAIT_TIMEOUT) return {EndpointRead::Status::Timeout, {}, ""};
                if (event != WAIT_OBJECT_0 + 2) throw std::runtime_error("Selected output audio event failed.");
                Check(capture_->GetNextPacketSize(&frames));
                if (!frames) return {EndpointRead::Status::Timeout, {}, ""};
            }
            BYTE* data = nullptr;
            DWORD flags = 0;
            UINT64 device = 0, qpc = 0;
            Check(capture_->GetBuffer(&data, &frames, &flags, &device, &qpc));
            PcmBlock block;
            try {
                // QPC packet positions are in 100 ns units, matching process capture.
                // DATA_DISCONTINUITY keeps the new packet's actual clock position,
                // exposing the gap rather than inventing a continuous sample time.
                const auto timestamp = (flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) || !qpc ? NowNs() : qpc * 100;
                block = {std::vector<float>(static_cast<std::size_t>(frames) * 2), 2, 48000, timestamp};
                if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && data) std::memcpy(block.samples.data(), data, block.samples.size() * sizeof(float));
            } catch (...) { capture_->ReleaseBuffer(frames); throw; }
            Check(capture_->ReleaseBuffer(frames));
            return {EndpointRead::Status::Data, std::move(block), ""};
        } catch (const std::exception& error) { return {EndpointRead::Status::Failed, {}, error.what()}; }
    }
    void Stop() override {
        if (client_) client_->Stop();
        capture_.Reset(); client_.Reset(); samples_.reset();
        if (initialized_) { CoUninitialize(); initialized_ = false; }
    }
};
}
std::unique_ptr<IEndpointCaptureClient> MakeEndpointCaptureClient() { return std::make_unique<WindowsEndpointCaptureClient>(); }
}
