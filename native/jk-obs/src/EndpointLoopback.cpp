#include "EndpointLoopback.hpp"
#include <windows.h>
#include <condition_variable>
#include <future>
#include <thread>

namespace jk {
struct EndpointLoopback::Impl {
    IEndpointCaptureClient& client;
    ProcessBinding& controller;
    AudioSink& sink;
    std::unique_ptr<void, decltype(&CloseHandle)> stop{CreateEventW(nullptr, TRUE, FALSE, nullptr), &CloseHandle};
    std::thread worker, emitter;
    std::atomic_bool running{false};
    mutable std::mutex mutex;
    std::mutex join_mutex;
    std::condition_variable ready;
    PcmQueue queue;
    std::string error;

    Impl(IEndpointCaptureClient& value, ProcessBinding& binding, AudioSink& output)
        : client(value), controller(binding), sink(output) {}
    bool Cancelled() const { return !controller.CanEmit() || WaitForSingleObject(stop.get(), 0) != WAIT_TIMEOUT; }
    void Cancel() { controller.Seal(); SetEvent(stop.get()); ready.notify_all(); }
    void Emit() {
        while (!Cancelled()) {
            std::optional<PcmBlock> block;
            {
                std::unique_lock lock(mutex);
                ready.wait_for(lock, std::chrono::milliseconds(20), [this] { return queue.DurationMs() > 0 || Cancelled(); });
                if (Cancelled()) break;
                block = queue.Pop();
            }
            if (block && (Cancelled() || !DeliverAudio(controller, sink, *block))) { Cancel(); break; }
        }
    }
    void Run(std::wstring id, std::promise<CaptureResult> started) {
        bool signaled = false;
        try {
            if (!stop || id.empty() || Cancelled()) throw std::runtime_error("Selected output or controller is unavailable.");
            const auto opened = client.Open(id);
            if (!opened.started) throw std::runtime_error(opened.error);
            if (Cancelled()) throw std::runtime_error("Selected output capture was cancelled during setup.");
            running = true;
            emitter = std::thread([this] { Emit(); });
            started.set_value({true, ""});
            signaled = true;
            while (!Cancelled()) {
                auto packet = client.Read(reinterpret_cast<uintptr_t>(stop.get()), controller.WaitHandle());
                if (Cancelled() || packet.status == EndpointRead::Status::Stopped) break;
                if (packet.status == EndpointRead::Status::Timeout) continue;
                if (packet.status == EndpointRead::Status::Failed) throw std::runtime_error(packet.error);
                if (packet.block.channels != 2 || packet.block.sample_rate != 48000) throw std::runtime_error("Invalid selected output PCM format.");
                {
                    std::lock_guard lock(mutex);
                    if (!Cancelled()) queue.Push(std::move(packet.block));
                }
                ready.notify_one();
            }
        } catch (const std::exception& failure) {
            { std::lock_guard lock(mutex); error = failure.what(); }
            if (!signaled) started.set_value({false, failure.what()});
        }
        Cancel();
        client.Stop(); // COM setup, reads, and cleanup stay on this worker apartment.
        if (emitter.joinable()) emitter.join();
        { std::lock_guard lock(mutex); queue.Clear(); }
        running = false;
    }
    void Join() {
        // Serialize concurrent Stop/initialization-timeout joins. Neither capture
        // worker uses this mutex; queue/registry/guard locks are never held here.
        std::lock_guard lock(join_mutex);
        if (worker.joinable()) worker.join();
    }
};
EndpointLoopback::EndpointLoopback(std::unique_ptr<IEndpointCaptureClient> client)
    : client_(client ? std::move(client) : MakeEndpointCaptureClient()) {}
EndpointLoopback::~EndpointLoopback() { Stop(); }
CaptureResult EndpointLoopback::Start(const std::wstring& id, ProcessBinding& controller, AudioSink& sink) {
    std::shared_ptr<Impl> state, previous;
    {
        std::lock_guard lock(mutex_);
        state = std::make_shared<Impl>(*client_, controller, sink);
        // The unique state pointer is this startup's reservation. Publish it
        // before old-worker teardown so Stop can cancel the pending generation.
        if (pending_) pending_->Cancel();
        pending_ = state;
        previous = impl_;
        if (previous) previous->Cancel();
    }
    if (previous) previous->Join();
#ifdef JK_ENDPOINT_STARTUP_BARRIER
    JK_ENDPOINT_STARTUP_BARRIER();
#endif
    std::promise<CaptureResult> promise;
    auto result = promise.get_future();
    {
        std::lock_guard lock(mutex_);
        if (pending_ != state || state->Cancelled()) {
            state->Cancel();
            if (pending_ == state) pending_.reset();
            return {false, "Selected output capture was cancelled before setup."};
        }
        impl_ = state;
        pending_.reset();
        // Publish and install atomically with respect to Stop's snapshot. The
        // worker alone accesses the client; old workers have already joined.
        state->worker = std::thread([state, id, promise = std::move(promise)]() mutable { state->Run(id, std::move(promise)); });
    }
    if (result.wait_for(std::chrono::seconds(6)) != std::future_status::ready) {
        state->Cancel();
        state->Join();
        return {false, "Selected output initialization timed out."};
    }
    return result.get();
}
void EndpointLoopback::Stop() {
    std::shared_ptr<Impl> state;
    {
        std::lock_guard lock(mutex_);
        if (pending_) { pending_->Cancel(); pending_.reset(); }
        state = impl_;
        if (state) state->Cancel();
    }
    if (state) state->Join();
}
bool EndpointLoopback::Running() const { std::lock_guard lock(mutex_); return impl_ && impl_->running.load(); }
std::string EndpointLoopback::Error() const {
    std::lock_guard lock(mutex_);
    if (!impl_) return {};
    std::lock_guard state_lock(impl_->mutex);
    return impl_->error;
}
AudioQueueStats EndpointLoopback::QueueStats() const {
    std::lock_guard lock(mutex_);
    if (!impl_) return {};
    std::lock_guard state_lock(impl_->mutex);
    return {impl_->queue.DurationMs(), impl_->queue.DroppedFrames()};
}
}
