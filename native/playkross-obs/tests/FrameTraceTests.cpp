#include "FrameTrace.hpp"
#include <iostream>
#include <string>
#include <cmath>
int main(int argc, char** argv) {
    if (argc != 2) return 10;
    std::uint64_t now = 100'000'000;
    jk::FrameTrace trace([&] { return now; });
    const std::string test = argv[1];
    if (test == "UnmatchedPtsHasUnavailableDelay") {
        trace.Encoded(99, 30'000'000); auto state = trace.Snapshot();
        if (state.encode_residence_ms || state.unmatched_packets != 1) return 1;
    } else if (test == "RepeatedRenderedFrameIsNotNewCapture") {
        trace.Capture(1, 1, 10); trace.Render(1, 7, 20); trace.Render(1, 8, 30);
        auto state = trace.Snapshot();
        if (state.captured_frames != 1 || state.rendered_frames != 2 || state.repeated_frames != 1) return 2;
    } else if (test == "DroppedAndReorderedPacketsRemainBounded") {
        now = 2'000'000'000;
        for (std::uint64_t i = 1; i <= 1000; i++) { trace.Capture(1, i, i * 1'000'000); trace.Render(i, static_cast<std::int64_t>(i), i * 1'000'000 + 1); trace.EncodeInput(static_cast<std::int64_t>(i), i * 1'000'000 + 2); }
        trace.Encoded(1000, 1'020'000'002); trace.Encoded(998, 1'020'000'002);
        auto state = trace.Snapshot();
        if (state.outstanding_frames > 512 || state.encoded_packets != 2 || !state.encode_residence_ms) return 3;
    } else if (test == "MonotonicTimingDoesNotUseWallClock") {
        trace.EncodeInput(1, 10'000'000); trace.Encoded(1, 30'000'000);
        auto state = trace.Snapshot();
        if (!state.encode_residence_ms || std::abs(*state.encode_residence_ms - 20) > .001) return 4;
        now = 60'100'000'001;
        if (trace.Snapshot().encode_residence_ms) return 5;
    } else if (test == "SourceResetCannotCorrelateOldFrames") {
        trace.Capture(1, 5, 10); trace.Render(5, 1, 20); trace.EncodeInput(1, 25);
        if (trace.Snapshot().outstanding_frames != 1) return 7;
        trace.Capture(2, 1, 30); trace.Encoded(1, 40);
        if (trace.Snapshot().encode_residence_ms) return 6;
    } else if (test == "CompositionMappingKeepsCaptureProvenance") {
        trace.Capture(1, 1, 10); trace.Render(1, 5000, 20);
        trace.RemapComposition(5000, 0); trace.EncodeInput(0, 30); trace.Encoded(0, 40);
        auto value = trace.Snapshot().capture_to_render_ms;
        if (!value || std::abs(*value - .00001) > .000001) return 8;
    } else if (test == "EncodeApiReturnIsDifferentFromPacketResidence") {
        trace.EncodeInput(0, 10'000'000); trace.EncodeReturn(0, 12'000'000); trace.Encoded(0, 30'000'000);
        const auto state = trace.Snapshot();
        if (!state.encode_api_call_ms || *state.encode_api_call_ms != 2 || !state.encode_residence_ms || *state.encode_residence_ms != 20) return 9;
    } else if (test == "FreshCaptureDelayExcludesRepeatedFrames") {
        trace.Capture(1, 1, 5'000'000); trace.Render(1, 1, 10'000'000); trace.EncodeInput(1, 15'000'000); trace.Encoded(1, 25'000'000);
        trace.Render(1, 2, 26'000'000); trace.EncodeInput(2, 27'000'000); trace.Encoded(2, 40'000'000);
        const auto state = trace.Snapshot();
        if (!state.capture_to_packet_ms || *state.capture_to_packet_ms != 20 || state.capture_timing_samples != 1 || !state.capture_to_render_ms || *state.capture_to_render_ms != 5) return 12;
    } else return 11;
    std::cout << "PASS: " << test << '\n'; return 0;
}
