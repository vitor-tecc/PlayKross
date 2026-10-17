#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmsystem.h>
#include "ProcessLoopback.hpp"
#include "EndpointLoopback.hpp"
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include <chrono>
#include <cmath>
#include <iostream>
#include <mutex>
#include <stdexcept>
#include <thread>

struct Sink final : jk::AudioSink {
    std::mutex mutex;
    std::uint64_t samples = 0;
    double squared = 0;
    void OnPcm(const jk::PcmBlock& block) override { std::lock_guard lock(mutex); samples += block.samples.size(); for (float value : block.samples) squared += value * value; }
};
static int Tone() {
    WAVEFORMATEX format{WAVE_FORMAT_PCM, 2, 48000, 192000, 4, 16, 0};
    HWAVEOUT output{};
    if (waveOutOpen(&output, WAVE_MAPPER, &format, 0, 0, CALLBACK_NULL) != MMSYSERR_NOERROR) return 2;
    std::vector<short> pcm(96000);
    for (int frame = 0; frame < 48000; ++frame) {
        const short value = static_cast<short>(700 * std::sin(2 * 3.141592653589793 * 997 * frame / 48000));
        pcm[frame * 2] = value; pcm[frame * 2 + 1] = value;
    }
    WAVEHDR header{}; header.lpData = reinterpret_cast<char*>(pcm.data()); header.dwBufferLength = static_cast<DWORD>(pcm.size() * sizeof(short));
    header.dwFlags = WHDR_BEGINLOOP | WHDR_ENDLOOP; header.dwLoops = 6;
    waveOutPrepareHeader(output, &header, sizeof(header)); waveOutWrite(output, &header, sizeof(header));
    Sleep(6500); waveOutReset(output); waveOutUnprepareHeader(output, &header, sizeof(header)); waveOutClose(output);
    return 0;
}
int main(int argc, char** argv) {
    if (argc == 2 && std::string(argv[1]) == "--tone") return Tone();
    wchar_t executable[32768]{}; GetModuleFileNameW(nullptr, executable, 32768);
    std::wstring command = L"\"" + std::wstring(executable) + L"\" --tone";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION child{};
    if (!CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &child)) return 2;
    const bool system = argc == 2 && std::string(argv[1]) == "--system-endpoint";
    std::wstring endpoint_id;
    if (system && SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) {
        // Opt-in fixture resolves one concrete ID once; capture never follows default changes.
        Microsoft::WRL::ComPtr<IMMDeviceEnumerator> enumerator;
        Microsoft::WRL::ComPtr<IMMDevice> device;
        LPWSTR id = nullptr;
        if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator))) &&
            SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &device)) && SUCCEEDED(device->GetId(&id))) endpoint_id = id;
        CoTaskMemFree(id); device.Reset(); enumerator.Reset(); CoUninitialize();
    }
    int result = 1;
    {
        auto token = jk::ReadProcessToken(child.dwProcessId);
        auto binding = token ? jk::ProcessBinding::Open(*token) : jk::BindingResult{nullptr, "No child identity"};
        Sink sink; jk::ProcessLoopback capture; jk::EndpointLoopback endpoint_capture;
        auto started = binding.binding ? (system ? endpoint_capture.Start(endpoint_id, *binding.binding, sink) : capture.Start(*binding.binding, sink)) : jk::CaptureResult{false, binding.error};
        if (!started.started) std::cerr << "FAIL: Selected audio capture failed: " << started.error << '\n';
        else {
            std::this_thread::sleep_for(std::chrono::seconds(2));
            capture.Stop(); endpoint_capture.Stop();
            std::lock_guard lock(sink.mutex);
            const double rms = sink.samples ? std::sqrt(sink.squared / sink.samples) : 0;
            if (sink.samples > 24000 && rms > 0.001 && !capture.Running() && !endpoint_capture.Running()) { std::cout << "PASS: selected capture synthetic tone, samples=" << sink.samples << ", RMS=" << rms << '\n'; result = 0; }
            else std::cerr << "FAIL: selected capture audio absent, samples=" << sink.samples << ", RMS=" << rms << '\n';
        }
    }
    WaitForSingleObject(child.hProcess, 8000);
    CloseHandle(child.hThread); CloseHandle(child.hProcess);
    return result;
}
