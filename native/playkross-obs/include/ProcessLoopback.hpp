#pragma once
#include "ProcessBinding.hpp"
#include <vector>
#include <deque>

namespace jk {
struct AudioQueueStats { double queued_ms; std::uint64_t dropped_frames; };
struct CaptureResult { bool started; std::string error; };
struct PcmBlock { std::vector<float> samples; std::uint32_t channels; std::uint32_t sample_rate; std::uint64_t monotonic_ns; };
struct AudioSink { virtual ~AudioSink() = default; virtual void OnPcm(const PcmBlock& block) = 0; };
class PcmQueue {
public:
    void Push(PcmBlock block);
    std::optional<PcmBlock> Pop();
    void Clear();
    double DurationMs() const;
    std::uint64_t DroppedFrames() const { return dropped_frames_; }
private:
    std::deque<PcmBlock> blocks_;
    std::uint64_t dropped_frames_ = 0;
};
bool DeliverAudio(ProcessBinding& binding, AudioSink& sink, const PcmBlock& block);
class ProcessLoopback {
public:
    ProcessLoopback();
    ~ProcessLoopback();
    CaptureResult Start(ProcessBinding& binding, AudioSink& sink);
    void Stop();
    bool Running() const;
    std::string Error() const;
    AudioQueueStats QueueStats() const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
