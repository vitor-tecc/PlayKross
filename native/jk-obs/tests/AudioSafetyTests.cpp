#include "ProcessLoopback.hpp"
#include "SceneGuard.hpp"
#include <iostream>
#include <map>
#include <functional>
#include <stdexcept>

static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
struct Lifetime final : jk::IProcessLifetime {
    bool alive = true;
    jk::ProcessToken Identity() const override { return {42, 1000, L"C:\\controller.exe"}; }
    bool Alive() const override { return alive; }
};
struct Probe final : jk::IProcessProbe {
    std::shared_ptr<Lifetime> lifetime = std::make_shared<Lifetime>();
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return lifetime; }
};
struct Sink final : jk::AudioSink { int emitted = 0; void OnPcm(const jk::PcmBlock&) override { ++emitted; } };
static jk::PcmBlock Block(std::uint64_t time) { return {std::vector<float>(960, 0.25f), 2, 48000, time}; }
static void OverflowDropsOldestPcmWithin200ms() {
    jk::PcmQueue queue;
    for (int i = 0; i < 22; ++i) queue.Push(Block(i * 10000000ULL));
    Require(queue.DurationMs() <= 200.001, "Audio queue must not grow beyond 200ms");
    Require(queue.DroppedFrames() == 960, "Overload must report discarded frames");
    Require(queue.Pop()->monotonic_ns == 20000000ULL, "Drop oldest, keep newest audio");
}
static void NoBufferAfterSeal() {
    Probe probe;
    auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
    binding.binding->Seal();
    Sink sink;
    Require(!jk::DeliverAudio(*binding.binding, sink, Block(0)), "Sealed source must reject PCM");
    Require(sink.emitted == 0, "No audio may reach output after seal");
}
static void RejectsDeviceAndSystemSources() {
    Probe probe;
    auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
    jk::SceneGuard guard;
    guard.Arm(std::move(binding.binding), "Application audio", 1);
    Require(!guard.Allows({"wasapi_input_capture", "Microphone", 1, false}), "Input devices must be prohibited");
    Require(!guard.Allows({"wasapi_output_capture", "Desktop", 1, false}), "System loopback must be prohibited");
    Require(!guard.Allows({"jk_process_audio", "Other app", 1, false}), "Unapproved app must be prohibited");
    Require(!guard.Allows({"jk_process_audio", "Application audio", 2, false}), "Stale generation must be prohibited");
    Require(!guard.Allows({"jk_process_audio", "Application audio", 1, true}), "Monitoring loop must be prohibited");
    Require(guard.Allows({"jk_process_audio", "Application audio", 1, false}), "Original approved app should be allowed");
}
static void GuardDisarmsOnControllerDeath() {
    Probe probe;
    auto binding = jk::ProcessBinding::Open(probe.lifetime->Identity(), probe);
    jk::SceneGuard guard;
    guard.Arm(std::move(binding.binding), "Application audio", 1);
    probe.lifetime->alive = false;
    Require(!guard.Active(), "Controller exit must disarm capture");
    Require(!guard.Allows({"jk_process_audio", "Application audio", 1, false}), "Controller exit must prohibit output");
}
int main(int argc, char** argv) {
    std::map<std::string, std::function<void()>> cases{{"OverflowDropsOldestPcmWithin200ms", OverflowDropsOldestPcmWithin200ms}, {"NoBufferAfterSeal", NoBufferAfterSeal}, {"RejectsDeviceAndSystemSources", RejectsDeviceAndSystemSources}, {"GuardDisarmsOnControllerDeath", GuardDisarmsOnControllerDeath}};
    try { cases.at(argc == 2 ? argv[1] : "")(); std::cout << "PASS " << argv[1] << '\n'; return 0; }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
