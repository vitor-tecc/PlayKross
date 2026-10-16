#include "FrameTrace.hpp"
#include <utility>
namespace jk {
FrameTrace::FrameTrace(std::function<std::uint64_t()> value) : clock(std::move(value)) { }
FrameTrace::Frame& FrameTrace::Get(std::int64_t pts) {
    if (auto found = frames.find(pts); found != frames.end()) return found->second;
    while (order.size() >= 512) {
        const auto oldest = order.front(); order.pop_front();
        if (auto found = frames.find(oldest.first); found != frames.end() && found->second.ticket == oldest.second) frames.erase(found);
    }
    const auto serial = ++ticket; order.emplace_back(pts, serial);
    auto& frame = frames[pts]; frame.ticket = serial; return frame;
}
void FrameTrace::Capture(std::uint64_t value, std::uint64_t seq, std::uint64_t ns) {
    std::lock_guard lock(mutex);
    if (!value || !seq) return;
    if (source != value || seq < sequence) { frames.clear(); order.clear(); samples.clear(); counters = {}; sequence = 0; last_rendered_sequence = 0; }
    source = value;
    if (seq > sequence) { counters.captured_frames += seq - sequence; sequence = seq; capture_ns = ns; }
}
void FrameTrace::Render(std::uint64_t seq, std::int64_t pts, std::uint64_t ns) {
    std::lock_guard lock(mutex);
    if (auto found = frames.find(pts); found != frames.end() && found->second.render) return;
    counters.rendered_frames++;
    const bool fresh = seq && seq != last_rendered_sequence;
    if (seq && seq == last_rendered_sequence) counters.repeated_frames++;
    last_rendered_sequence = seq;
    auto& frame = Get(pts); frame.render = ns; frame.fresh = fresh;
    if (seq && seq == sequence && capture_ns <= ns) frame.capture = capture_ns;
}
void FrameTrace::EncodeInput(std::int64_t pts, std::uint64_t ns) { std::lock_guard lock(mutex); Get(pts).input = ns; }
void FrameTrace::EncodeReturn(std::int64_t pts, std::uint64_t ns) { std::lock_guard lock(mutex); Get(pts).returned = ns; }
void FrameTrace::RemapComposition(std::int64_t composition_pts, std::int64_t encoded_pts) {
    std::lock_guard lock(mutex);
    if (composition_pts == encoded_pts) return;
    if (auto found = frames.find(composition_pts); found != frames.end()) {
        const auto copy = found->second; frames.erase(found);
        auto& mapped = Get(encoded_pts); mapped.capture = copy.capture; mapped.render = copy.render; mapped.fresh = copy.fresh;
    }
}
void FrameTrace::Encoded(std::int64_t pts, std::uint64_t ns) {
    std::lock_guard lock(mutex); counters.encoded_packets++;
    const auto found = frames.find(pts);
    if (found == frames.end() || !found->second.input || *found->second.input > ns) { counters.unmatched_packets++; if (found != frames.end()) frames.erase(found); return; }
    const auto frame = found->second; frames.erase(found);
    Sample sample{ns, static_cast<double>(ns - *frame.input) / 1'000'000.0, std::nullopt, std::nullopt, std::nullopt};
    if (frame.fresh && frame.capture && frame.render && *frame.render >= *frame.capture && ns >= *frame.capture) {
        sample.capture_render = static_cast<double>(*frame.render - *frame.capture) / 1'000'000.0;
        sample.capture_packet = static_cast<double>(ns - *frame.capture) / 1'000'000.0;
    }
    if (frame.returned && *frame.returned >= *frame.input && *frame.returned <= ns) sample.api = static_cast<double>(*frame.returned - *frame.input) / 1'000'000.0;
    samples.push_back(sample); if (samples.size() > 4096) samples.pop_front();
}
TraceSnapshot FrameTrace::Snapshot() {
    std::lock_guard lock(mutex); const auto now = clock();
    while (!samples.empty() && (samples.front().ns > now || now - samples.front().ns > 60'000'000'000ULL)) samples.pop_front();
    TraceSnapshot result = counters; result.outstanding_frames = frames.size(); result.sample_count = samples.size();
    double encode_sum = 0, capture_sum = 0, api_sum = 0, capture_packet_sum = 0; std::size_t encode_count = 0, capture_count = 0, api_count = 0;
    for (const auto& sample : samples) {
        if (sample.encode) { encode_sum += *sample.encode; encode_count++; }
        if (sample.capture_render && sample.capture_packet) { capture_sum += *sample.capture_render; capture_packet_sum += *sample.capture_packet; capture_count++; }
        if (sample.api) { api_sum += *sample.api; api_count++; }
    }
    if (encode_count) result.encode_residence_ms = encode_sum / static_cast<double>(encode_count);
    result.capture_timing_samples = capture_count;
    if (capture_count) { result.capture_to_render_ms = capture_sum / static_cast<double>(capture_count); result.capture_to_packet_ms = capture_packet_sum / static_cast<double>(capture_count); }
    if (api_count) result.encode_api_call_ms = api_sum / static_cast<double>(api_count);
    return result;
}
}
