#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "EndpointLoopback.hpp"
#include <condition_variable>
#include <chrono>
#include <functional>
#include <iostream>
#include <map>
#include <mutex>
#include <thread>

// Compile actual adapter logic with one scheduling seam. Normal library/plugin
// compilation contains no hook; the public capture interface is unchanged.
static std::function<void()> startup_barrier;
#define JK_ENDPOINT_STARTUP_BARRIER() do { if (startup_barrier) startup_barrier(); } while (false)
#include "../src/EndpointLoopback.cpp"
#undef JK_ENDPOINT_STARTUP_BARRIER

using namespace std::chrono_literals;
static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
template<class F> static void Until(F check) {
    for (int i = 0; i < 1000; ++i) {
        if (check()) return;
        std::this_thread::sleep_for(1ms);
    }
    throw std::runtime_error("Timed out waiting for capture state");
}
struct Lifetime final : jk::IProcessLifetime {
    std::atomic_bool alive{true};
    jk::ProcessToken Identity() const override { return {42, 1000, L"C:\\controller.exe"}; }
    bool Alive() const override { return alive.load(); }
};
struct Probe final : jk::IProcessProbe {
    std::shared_ptr<Lifetime> process = std::make_shared<Lifetime>();
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return process; }
};
struct Client final : jk::IEndpointCaptureClient {
    std::mutex mutex;
    std::condition_variable cv;
    std::vector<std::wstring> opened;
    std::deque<jk::EndpointRead> packets;
    bool fail_open = false, block_open = false, release_open = false, entered = false;
    std::atomic_bool stopped{false};
    std::atomic_uint reads{0};
    jk::CaptureResult Open(std::wstring_view id) override {
        std::unique_lock lock(mutex);
        opened.emplace_back(id);
        entered = true;
        cv.notify_all();
        cv.wait(lock, [this] { return !block_open || release_open; });
        return {!fail_open, fail_open ? "Selected endpoint unavailable" : ""};
    }
    jk::EndpointRead Read(uintptr_t stop, uintptr_t) override {
        ++reads;
        if (WaitForSingleObject(reinterpret_cast<HANDLE>(stop), 0) != WAIT_TIMEOUT)
            return {jk::EndpointRead::Status::Stopped, {}, ""};
        std::lock_guard lock(mutex);
        if (packets.empty()) return {jk::EndpointRead::Status::Timeout, {}, ""};
        auto result = std::move(packets.front());
        packets.pop_front();
        return result;
    }
    void Stop() override { stopped = true; }
    void Packet(float value = 0, std::size_t frames = 480) {
        std::lock_guard lock(mutex);
        packets.push_back({jk::EndpointRead::Status::Data,
            {std::vector<float>(frames * 2, value), 2, 48000, 1000000}, ""});
    }
    void Fail() {
        std::lock_guard lock(mutex);
        packets.push_back({jk::EndpointRead::Status::Failed, {}, "Endpoint invalidated"});
    }
};
struct Sink final : jk::AudioSink {
    std::mutex mutex;
    std::condition_variable cv;
    std::vector<jk::PcmBlock> blocks;
    bool hold = false, entered = false, released = false;
    void OnPcm(const jk::PcmBlock& block) override {
        std::unique_lock lock(mutex);
        blocks.push_back(block);
        entered = true;
        cv.notify_all();
        cv.wait(lock, [this] { return !hold || released; });
    }
    std::size_t Count() { std::lock_guard lock(mutex); return blocks.size(); }
    void Release() { { std::lock_guard lock(mutex); released = true; } cv.notify_all(); }
};
struct Fixture {
    Probe probe;
    std::unique_ptr<jk::ProcessBinding> binding;
    Sink sink;
    Client* client;
    jk::EndpointLoopback capture;
    Fixture() : client(new Client), capture(std::unique_ptr<jk::IEndpointCaptureClient>(client)) {
        auto result = jk::ProcessBinding::Open(probe.process->Identity(), probe);
        binding = std::move(result.binding);
    }
    ~Fixture() {
        sink.Release();
        { std::lock_guard lock(client->mutex); client->release_open = true; }
        client->cv.notify_all();
        capture.Stop();
    }
    void Start() { Require(capture.Start(L"render:selected", *binding, sink).started, "Selected endpoint must start"); }
};
static void PinnedEndpointDoesNotFollowDefault() {
    Fixture f;
    f.Start();
    f.client->Packet(.25f);
    Until([&] { return f.sink.Count() == 1; });
    f.capture.Stop();
    Require(f.client->opened == std::vector<std::wstring>{L"render:selected"},
        "Capture must only open explicitly selected endpoint");
}
static void CaptureOpenFailureHasNoFallback() {
    Fixture f;
    f.client->fail_open = true;
    Require(!f.capture.Start(L"render:missing", *f.binding, f.sink).started, "Open failure must fail capture");
    f.capture.Stop();
    Require(f.client->opened == std::vector<std::wstring>{L"render:missing"} &&
        f.sink.Count() == 0 && !f.capture.Error().empty(), "Open failure must remain silent without fallback");
}
static void QuietPacketsAreValid() {
    Fixture f;
    f.Start();
    f.client->Packet();
    Until([&] { return f.sink.Count() == 1; });
    f.capture.Stop();
    Require(f.sink.blocks[0].samples == std::vector<float>(960, 0) && f.capture.Error().empty(),
        "Silent PCM is valid audio");
}
static void ReadFailureStopsAndClearsQueue() {
    Fixture f;
    f.sink.hold = true;
    f.Start();
    f.client->Packet(-1);
    Until([&] { std::lock_guard lock(f.sink.mutex); return f.sink.entered; });

    // The emitter stays inside its first callback while a real backlog accumulates.
    for (int i = 1; i <= 5; ++i) f.client->Packet(static_cast<float>(i));
    Until([&] { return f.capture.QueueStats().queued_ms == 50; });
    f.client->Fail();
    Until([&] { return !f.binding->CanEmit() && f.client->stopped.load(); });
    f.sink.Release();
    f.capture.Stop();

    Require(!f.capture.Error().empty() && !f.capture.Running() &&
        f.capture.QueueStats().queued_ms == 0 && f.client->stopped,
        "Read failure must stop client and discard the populated queue");
    Require(f.sink.Count() == 1, "No queued packet may escape after capture failure");
}
static void ControllerDeathAndSealStopEmission() {
    for (bool death : {false, true}) {
        Fixture f;
        f.Start();
        if (death) f.probe.process->alive = false;
        else f.binding->Seal();
        f.client->Packet(.5f);
        Until([&] { return !f.capture.Running(); });
        f.capture.Stop();
        Require(f.sink.Count() == 0 && f.capture.QueueStats().queued_ms == 0,
            "Dead or old sealed controller must emit zero blocks");
    }
}
static void StopDuringStartCannotPublishLatePackets() {
    Fixture f;
    f.client->block_open = true;
    jk::CaptureResult result{};
    std::thread start([&] { result = f.capture.Start(L"render:selected", *f.binding, f.sink); });
    {
        std::unique_lock lock(f.client->mutex);
        f.client->cv.wait(lock, [&] { return f.client->entered; });
    }
    std::thread stop([&] { f.capture.Stop(); });
    Until([&] { return !f.binding->CanEmit(); });
    f.client->Packet(.5f);
    { std::lock_guard lock(f.client->mutex); f.client->release_open = true; }
    f.client->cv.notify_all();
    start.join();
    stop.join();
    Require(!result.started && !f.capture.Running() && f.sink.Count() == 0 && f.capture.QueueStats().queued_ms == 0,
        "Stop during Open must cancel startup and late packets");
}
static void StopBeforeWorkerInstallCancelsPendingStart() {
    Fixture f;
    std::mutex mutex;
    std::condition_variable cv;
    bool pending = false, resume = false;
    startup_barrier = [&] {
        std::unique_lock lock(mutex);
        pending = true;
        cv.notify_all();
        cv.wait(lock, [&] { return resume; });
    };
    jk::CaptureResult result{};
    std::thread start([&] { result = f.capture.Start(L"render:selected", *f.binding, f.sink); });
    {
        std::unique_lock lock(mutex);
        cv.wait(lock, [&] { return pending; });
    }
    // Start is past old-state teardown but cannot install or open a worker yet.
    f.capture.Stop();
    const bool sealed_when_stop_returned = !f.binding->CanEmit();
    { std::lock_guard lock(mutex); resume = true; }
    cv.notify_all();
    start.join();
    startup_barrier = {};
    const bool running_after_start = f.capture.Running();
    f.capture.Stop();

    Require(sealed_when_stop_returned && !result.started,
        "Stop before worker installation must cancel and seal the pending Start");
    Require(f.client->opened.empty() && f.client->reads == 0 && f.sink.Count() == 0,
        "Cancelled pending generation must never open, read, or deliver PCM");
    Require(!running_after_start && f.capture.QueueStats().queued_ms == 0,
        "Cancelled pending generation must stay stopped with an empty queue");
}
static void QueueOverflowDropsOldestWithin200ms() {
    Fixture f;
    f.sink.hold = true;
    f.Start();
    f.client->Packet(-1);
    Until([&] { std::lock_guard lock(f.sink.mutex); return f.sink.entered; });
    for (int i = 1; i <= 30; ++i) f.client->Packet(static_cast<float>(i));
    Until([&] { return f.capture.QueueStats().dropped_frames >= 4800; });
    const auto stats = f.capture.QueueStats();
    Require(stats.queued_ms <= 200.000001 && stats.dropped_frames == 4800,
        "Overload must drop oldest frames and bound queue to 200 ms");
    f.sink.Release();
    Until([&] { return f.sink.Count() == 21; });
    f.capture.Stop();
    Require(f.sink.blocks[1].samples[0] == 11 && f.capture.QueueStats().queued_ms == 0,
        "Oldest queued blocks must be discarded; stop clears queue");
}
int main(int argc, char** argv) {
    const std::map<std::string, std::function<void()>> cases{
        {"PinnedEndpointDoesNotFollowDefault", PinnedEndpointDoesNotFollowDefault},
        {"CaptureOpenFailureHasNoFallback", CaptureOpenFailureHasNoFallback},
        {"QuietPacketsAreValid", QuietPacketsAreValid},
        {"ReadFailureStopsAndClearsQueue", ReadFailureStopsAndClearsQueue},
        {"ControllerDeathAndSealStopEmission", ControllerDeathAndSealStopEmission},
        {"StopDuringStartCannotPublishLatePackets", StopDuringStartCannotPublishLatePackets},
        {"StopBeforeWorkerInstallCancelsPendingStart", StopBeforeWorkerInstallCancelsPendingStart},
        {"QueueOverflowDropsOldestWithin200ms", QueueOverflowDropsOldestWithin200ms}};
    try {
        cases.at(argc == 2 ? argv[1] : "")();
        std::cout << "PASS " << argv[1] << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
