#include "ProcessLoopback.hpp"
#include <stdexcept>
namespace jk {
void PcmQueue::Push(PcmBlock block) {
    if (!block.channels || !block.sample_rate || block.samples.size() % block.channels != 0) throw std::invalid_argument("Invalid PCM format");
    blocks_.push_back(std::move(block));
    while (DurationMs() > 200.000001 && !blocks_.empty()) {
        dropped_frames_ += blocks_.front().samples.size() / blocks_.front().channels;
        blocks_.pop_front();
    }
}
std::optional<PcmBlock> PcmQueue::Pop() { if (blocks_.empty()) return std::nullopt; auto block = std::move(blocks_.front()); blocks_.pop_front(); return block; }
void PcmQueue::Clear() { blocks_.clear(); }
double PcmQueue::DurationMs() const { double ms = 0; for (const auto& block : blocks_) ms += 1000.0 * block.samples.size() / block.channels / block.sample_rate; return ms; }
bool DeliverAudio(ProcessBinding& binding, AudioSink& sink, const PcmBlock& block) {
    if (!binding.CanEmit()) return false;
    sink.OnPcm(block);
    return true;
}
}
