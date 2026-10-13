import { describe, expect, it } from "vitest";
import { collectViewerDiagnostics, type StatsSnapshot, type PresentationSample } from "../src/media/playerDiagnostics";
const presentation: PresentationSample = { frames: 40, elapsedMs: 2000, supported: true, startupMs: 100, stalls: 1, longestGapMs: 600 };
const snapshot = (timeMs: number, entries: Record<string, unknown>[], epoch = "first"): StatsSnapshot => ({ timeMs, epoch, stats: new Map(entries.map((entry, index) => [String(entry.id ?? index), entry])) });
describe("viewer diagnostic provenance", () => {
  it("MissingStatsStayNull", () => {
    const current = collectViewerDiagnostics(null, snapshot(1000, []), { ...presentation, supported: false });
    expect(current.captureFps).toBeNull(); expect(current.decodedFps).toBeNull(); expect(current.presentedFps).toBeNull(); expect(current.receivedMbps).toBeNull(); expect(current.connectionPath).toBe("unknown");
  });
  it("ReconnectResetsDeltas", () => {
    const previous = snapshot(1000, [{ id: "video", type: "inbound-rtp", kind: "video", bytesReceived: 1000, framesDecoded: 50 }]);
    const current = snapshot(2000, [{ id: "video", type: "inbound-rtp", kind: "video", bytesReceived: 10, framesDecoded: 2 }], "second");
    const result = collectViewerDiagnostics(previous, current, presentation); expect(result.receivedMbps).toBeNull(); expect(result.decodedFps).toBeNull();
  });
  it("DecodedIsNotPresentedFpsAndBytesHaveCorrectUnits", () => {
    const previous = snapshot(1000, [{ id: "video", type: "inbound-rtp", kind: "video", bytesReceived: 0, framesDecoded: 100 }]);
    const current = snapshot(3000, [{ id: "video", type: "inbound-rtp", kind: "video", bytesReceived: 10_000_000, framesDecoded: 220 }]);
    const result = collectViewerDiagnostics(previous, current, presentation); expect(result.receivedMbps).toBe(40); expect(result.decodedFps).toBe(60); expect(result.presentedFps).toBe(20);
  });
  it("RttIsNotVideoLatency", () => {
    const current = snapshot(3000, [{ id: "transport", type: "transport", selectedCandidatePairId: "pair" }, { id: "pair", type: "candidate-pair", currentRoundTripTime: .1 }]);
    const result = collectViewerDiagnostics(null, current, presentation); expect(result.iceRttMs).toBe(100); expect(result.videoLatencyMs).toBeNull();
  });
  it("RelayRequiresSelectedCandidateEvidence", () => {
    const candidates = [{ id: "pair", type: "candidate-pair", nominated: true, state: "succeeded", localCandidateId: "local", remoteCandidateId: "remote" }, { id: "local", type: "local-candidate", candidateType: "relay" }, { id: "remote", type: "remote-candidate", candidateType: "host" }];
    expect(collectViewerDiagnostics(null, snapshot(1000, candidates), presentation).connectionPath).toBe("unknown");
    expect(collectViewerDiagnostics(null, snapshot(1000, [...candidates, { id: "transport", type: "transport", selectedCandidatePairId: "pair" }]), presentation).connectionPath).toBe("relayed");
  });
});
