#pragma once
#include <cstdint>
#include <functional>
#include <optional>
#include <cstddef>
#include <map>
#include <deque>
#include <mutex>
namespace jk {
struct TraceSnapshot {
    std::uint64_t captured_frames{}, rendered_frames{}, repeated_frames{}, encoded_packets{}, unmatched_packets{};
    std::size_t outstanding_frames{}, sample_count{}, capture_timing_samples{};
    std::optional<double> encode_residence_ms, capture_to_render_ms, encode_api_call_ms, capture_to_packet_ms;
};
class FrameTrace {
public:
    explicit FrameTrace(std::function<std::uint64_t()> clock);
    void Capture(std::uint64_t source, std::uint64_t sequence, std::uint64_t ns);
    void Render(std::uint64_t sequence, std::int64_t pts, std::uint64_t ns);
    void EncodeInput(std::int64_t pts, std::uint64_t ns);
    void EncodeReturn(std::int64_t pts, std::uint64_t ns);
    void RemapComposition(std::int64_t composition_pts, std::int64_t encoded_pts);
    void Encoded(std::int64_t pts, std::uint64_t ns);
    TraceSnapshot Snapshot();
private:
    struct Frame { std::uint64_t ticket{}; std::optional<std::uint64_t> capture, render, input, returned; bool fresh = false; };
    struct Sample { std::uint64_t ns; std::optional<double> encode, capture_render, api, capture_packet; };
    Frame& Get(std::int64_t pts);
    std::mutex mutex;
    std::function<std::uint64_t()> clock;
    std::map<std::int64_t, Frame> frames;
    std::deque<std::pair<std::int64_t, std::uint64_t>> order;
    std::deque<Sample> samples;
    TraceSnapshot counters;
    std::uint64_t source{}, sequence{}, capture_ns{}, last_rendered_sequence{}, ticket{};
};
}
