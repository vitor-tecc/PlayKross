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
using namespace std::chrono_literals;
static void Require(bool ok, const char* why) { if (!ok) throw std::runtime_error(why); }
template<class F> static void Until(F check) { for (int i=0;i<1000;++i) { if(check()) return; std::this_thread::sleep_for(1ms); } throw std::runtime_error("Timed out waiting for capture state"); }
struct Lifetime final : jk::IProcessLifetime {
    std::atomic_bool alive{true};
    jk::ProcessToken Identity() const override { return {42,1000,L"C:\\controller.exe"}; }
    bool Alive() const override { return alive.load(); }
};
struct Probe final : jk::IProcessProbe {
    std::shared_ptr<Lifetime> process=std::make_shared<Lifetime>();
    std::shared_ptr<jk::IProcessLifetime> Open(std::uint32_t) override { return process; }
};
struct Client final : jk::IEndpointCaptureClient {
    std::mutex mutex; std::condition_variable cv;
    std::vector<std::wstring> opened;
    std::deque<jk::EndpointRead> packets;
    bool fail_open=false, block_open=false, release_open=false, entered=false;
    std::atomic_bool stopped{false};
    jk::CaptureResult Open(std::wstring_view id) override {
        std::unique_lock lock(mutex); opened.emplace_back(id); entered=true; cv.notify_all();
        cv.wait(lock,[this]{return !block_open || release_open;});
        return {!fail_open,fail_open?"Selected endpoint unavailable":""};
    }
    jk::EndpointRead Read(uintptr_t stop, uintptr_t) override {
        if (WaitForSingleObject(reinterpret_cast<HANDLE>(stop),0)!=WAIT_TIMEOUT) return {jk::EndpointRead::Status::Stopped,{},""};
        std::lock_guard lock(mutex);
        if(packets.empty()) { return {jk::EndpointRead::Status::Timeout,{},""}; }
        auto result=std::move(packets.front()); packets.pop_front(); return result;
    }
    void Stop() override { stopped=true; }
    void Packet(float value=0, std::size_t frames=480) { std::lock_guard lock(mutex); packets.push_back({jk::EndpointRead::Status::Data,{std::vector<float>(frames*2,value),2,48000,1000000},""}); }
    void Fail() { std::lock_guard lock(mutex); packets.push_back({jk::EndpointRead::Status::Failed,{},"Endpoint invalidated"}); }
};
struct Sink final : jk::AudioSink {
    std::mutex mutex; std::condition_variable cv; std::vector<jk::PcmBlock> blocks;
    bool hold=false, entered=false, released=false;
    void OnPcm(const jk::PcmBlock& b) override { std::unique_lock lock(mutex); blocks.push_back(b); entered=true; cv.notify_all(); cv.wait(lock,[this]{return !hold||released;}); }
    std::size_t Count() { std::lock_guard lock(mutex); return blocks.size(); }
};
struct Fixture {
    Probe probe; std::unique_ptr<jk::ProcessBinding> binding; Sink sink; Client* client;
    jk::EndpointLoopback capture;
    Fixture():client(new Client),capture(std::unique_ptr<jk::IEndpointCaptureClient>(client)) { auto b=jk::ProcessBinding::Open(probe.process->Identity(),probe); binding=std::move(b.binding); }
    ~Fixture() { {std::lock_guard lock(sink.mutex);sink.released=true;}sink.cv.notify_all(); {std::lock_guard lock(client->mutex);client->release_open=true;}client->cv.notify_all(); capture.Stop(); }
    void Start() { Require(capture.Start(L"render:selected",*binding,sink).started,"Selected endpoint must start"); }
};
static void PinnedEndpointDoesNotFollowDefault() { Fixture f; f.Start(); f.client->Packet(.25f); Until([&]{return f.sink.Count()==1;}); f.capture.Stop(); Require(f.client->opened==std::vector<std::wstring>{L"render:selected"},"Capture must only open explicitly selected endpoint"); }
static void CaptureOpenFailureHasNoFallback() { Fixture f; f.client->fail_open=true; Require(!f.capture.Start(L"render:missing",*f.binding,f.sink).started,"Open failure must fail capture"); f.capture.Stop(); Require(f.client->opened==std::vector<std::wstring>{L"render:missing"}&&f.sink.Count()==0&&!f.capture.Error().empty(),"Open failure must remain silent without fallback"); }
static void QuietPacketsAreValid() { Fixture f; f.Start(); f.client->Packet(); Until([&]{return f.sink.Count()==1;}); f.capture.Stop(); Require(f.sink.blocks[0].samples==std::vector<float>(960,0)&&f.capture.Error().empty(),"Silent PCM is valid audio"); }
static void ReadFailureStopsAndClearsQueue() { Fixture f; f.Start(); f.client->Fail(); Until([&]{return !f.capture.Running();}); f.capture.Stop(); Require(!f.capture.Error().empty()&&f.capture.QueueStats().queued_ms==0&&f.client->stopped,"Read failure must stop client and clear queued PCM"); }
static void ControllerDeathAndSealStopEmission() { for(bool death:{false,true}) { Fixture f; f.Start(); if(death)f.probe.process->alive=false;else f.binding->Seal(); f.client->Packet(.5f); Until([&]{return !f.capture.Running();}); f.capture.Stop(); Require(f.sink.Count()==0&&f.capture.QueueStats().queued_ms==0,"Dead or old sealed controller must emit zero blocks"); } }
static void StopDuringStartCannotPublishLatePackets() { Fixture f; f.client->block_open=true; jk::CaptureResult result{}; std::thread start([&]{result=f.capture.Start(L"render:selected",*f.binding,f.sink);}); {std::unique_lock lock(f.client->mutex);f.client->cv.wait(lock,[&]{return f.client->entered;});} std::thread stop([&]{f.capture.Stop();}); Until([&]{return !f.binding->CanEmit();}); f.client->Packet(.5f); {std::lock_guard lock(f.client->mutex);f.client->release_open=true;} f.client->cv.notify_all(); start.join(); stop.join(); Require(!result.started&&!f.capture.Running()&&f.sink.Count()==0&&f.capture.QueueStats().queued_ms==0,"Stop during Open must cancel startup and late packets"); }
static void QueueOverflowDropsOldestWithin200ms() { Fixture f; f.sink.hold=true; f.Start(); f.client->Packet(-1); Until([&]{std::lock_guard lock(f.sink.mutex);return f.sink.entered;}); for(int i=1;i<=30;++i)f.client->Packet(static_cast<float>(i)); Until([&]{return f.capture.QueueStats().dropped_frames>=4800;}); auto stats=f.capture.QueueStats(); Require(stats.queued_ms<=200.000001&&stats.dropped_frames==4800,"Overload must drop oldest frames and bound queue to 200 ms"); {std::lock_guard lock(f.sink.mutex);f.sink.released=true;}f.sink.cv.notify_all(); Until([&]{return f.sink.Count()==21;}); f.capture.Stop(); Require(f.sink.blocks[1].samples[0]==11&&f.capture.QueueStats().queued_ms==0,"Oldest queued blocks must be discarded; stop clears queue"); }
int main(int argc,char** argv) { std::map<std::string,std::function<void()>> cases{{"PinnedEndpointDoesNotFollowDefault",PinnedEndpointDoesNotFollowDefault},{"CaptureOpenFailureHasNoFallback",CaptureOpenFailureHasNoFallback},{"QuietPacketsAreValid",QuietPacketsAreValid},{"ReadFailureStopsAndClearsQueue",ReadFailureStopsAndClearsQueue},{"ControllerDeathAndSealStopEmission",ControllerDeathAndSealStopEmission},{"StopDuringStartCannotPublishLatePackets",StopDuringStartCannotPublishLatePackets},{"QueueOverflowDropsOldestWithin200ms",QueueOverflowDropsOldestWithin200ms}};try{cases.at(argc==2?argv[1]:"")();std::cout<<"PASS "<<argv[1]<<'\n';return 0;}catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<'\n';return 1;} }
