export interface StatsSnapshot { epoch: string; timeMs: number; stats: ReadonlyMap<string, Record<string, unknown>> }
export interface PresentationSample { frames: number; elapsedMs: number; supported: boolean; startupMs: number | null; stalls: number; longestGapMs: number | null }
export interface ViewerDiagnostics { captureFps: null; receivedMbps: number | null; decodedFps: number | null; presentedFps: number | null; iceRttMs: number | null; videoLatencyMs: null; connectionPath: "unknown" | "direct" | "relayed"; packetLossPercent: number | null; jitterMs: number | null; codec: string | null; width: number | null; height: number | null; startupMs: number | null; stalls: number; longestGapMs: number | null }
const number = (value: unknown): number | null => typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : null;
export function collectViewerDiagnostics(previous: StatsSnapshot | null, current: StatsSnapshot, presentation: PresentationSample): ViewerDiagnostics {
  const values = [...current.stats.values()];
  const video = values.find(s => s.type === "inbound-rtp" && (s.kind === "video" || s.mediaType === "video"));
  const old = previous?.epoch === current.epoch && video ? previous.stats.get(String(video.id)) : undefined;
  const elapsed = previous ? current.timeMs - previous.timeMs : 0;
  const delta = (key: string) => {
    const a = number(old?.[key]), b = number(video?.[key]);
    return a !== null && b !== null && b >= a && old?.ssrc === video?.ssrc && elapsed > 0 ? b - a : null;
  };
  const rate = (key: string, scale: number) => { const d = delta(key); return d === null ? null : d * scale / elapsed; };
  const transport = values.find(s => s.type === "transport" && typeof s.selectedCandidatePairId === "string");
  const pair = transport ? current.stats.get(String(transport.selectedCandidatePairId)) : values.find(s => s.type === "candidate-pair" && s.selected === true);
  const local = pair ? current.stats.get(String(pair.localCandidateId)) : undefined;
  const remote = pair ? current.stats.get(String(pair.remoteCandidateId)) : undefined;
  const directTypes = ["host", "srflx", "prflx"];
  const path = !pair ? "unknown" : local?.candidateType === "relay" || remote?.candidateType === "relay" ? "relayed" : directTypes.includes(String(local?.candidateType)) && directTypes.includes(String(remote?.candidateType)) ? "direct" : "unknown";
  const received = delta("packetsReceived"), lost = delta("packetsLost");
  const rtt = number(pair?.currentRoundTripTime), jitter = number(video?.jitter);
  const codec = video ? current.stats.get(String(video.codecId)) : undefined;
  return { captureFps: null, receivedMbps: rate("bytesReceived", .008), decodedFps: rate("framesDecoded", 1000),
    presentedFps: presentation.supported && presentation.elapsedMs > 0 ? presentation.frames * 1000 / presentation.elapsedMs : null,
    iceRttMs: rtt === null ? null : rtt * 1000, videoLatencyMs: null, connectionPath: path,
    packetLossPercent: received !== null && lost !== null && received + lost > 0 ? lost * 100 / (received + lost) : null,
    jitterMs: jitter === null ? null : jitter * 1000, codec: typeof codec?.mimeType === "string" ? codec.mimeType : null,
    width: number(video?.frameWidth), height: number(video?.frameHeight), startupMs: presentation.startupMs, stalls: presentation.stalls, longestGapMs: presentation.longestGapMs };
}

// One connection owns this sampler. Its bounded five-second history is discarded on reconnect.
export function observePlayer(peer: RTCPeerConnection, video: HTMLVideoElement, changed: (sample: ViewerDiagnostics) => void): () => void {
  const epoch = crypto.randomUUID(), start = performance.now();
  const snapshots: StatsSnapshot[] = [], frames: { time: number; count: number }[] = [];
  let stopped = false, callback = 0, timer: ReturnType<typeof setTimeout>;
  let first: number | null = null, last: number | null = null, stalls = 0, longest: number | null = null, gapCounted = false;
  const eligible = () => !video.paused && !video.ended && document.visibilityState === "visible";
  const supported = typeof video.requestVideoFrameCallback === "function";
  const frame: VideoFrameRequestCallback = (now, metadata) => {
    if (stopped) return;
    if (eligible()) {
      if (first === null) first = now - start;
      if (last !== null) { const gap = now - last; longest = Math.max(longest ?? 0, gap); if (gap > 500 && !gapCounted) stalls++; }
      last = now; gapCounted = false;
      frames.push({ time: now, count: metadata.presentedFrames });
      while (frames.length > 512 || frames.length > 1 && frames[1]!.time < now - 5000) frames.shift();
    } else { last = null; frames.length = 0; gapCounted = false; }
    callback = video.requestVideoFrameCallback(frame);
  };
  if (supported) callback = video.requestVideoFrameCallback(frame);
  const sample = async () => {
    try {
      const report = await peer.getStats(); if (stopped) return;
      const now = performance.now();
      const stats = new Map<string, Record<string, unknown>>(); report.forEach(s => stats.set(s.id, s));
      const current = { epoch, timeMs: now, stats }; snapshots.push(current);
      while (snapshots.length > 7 || snapshots.length > 1 && snapshots[1]!.timeMs < now - 5000) snapshots.shift();
      if (!eligible()) { frames.length = 0; last = null; gapCounted = false; }
      if (last !== null && now - last > 500) { longest = Math.max(longest ?? 0, now - last); if (!gapCounted) { stalls++; gapCounted = true; } }
      const earliest = frames[0], latest = frames.at(-1);
      changed(collectViewerDiagnostics(snapshots.length > 1 ? snapshots[0]! : null, current, {
        // Include the time since the last presentation so a freeze produces falling FPS.
        frames: earliest && latest ? latest.count - earliest.count : 0, elapsedMs: earliest ? now - earliest.time : 0,
        supported: supported && earliest !== undefined, startupMs: first, stalls, longestGapMs: longest }));
    } catch { /* A closing peer cannot supply observations; its owner resets the panel. */ }
    if (!stopped) timer = setTimeout(() => { void sample(); }, 1000);
  };
  void sample();
  return () => { stopped = true; clearTimeout(timer); if (supported) video.cancelVideoFrameCallback(callback); };
}
