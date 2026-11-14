import type { PlaybackGrant } from "../api/contracts";
import { deleteResource, hasCandidate, isMediaResource, isSignalingEndpoint, parseIceServers, signalingRequest, superviseConnection, supportsCodec, waitForPeer, type OwnedResource } from "./signaling";
export { parseIceServers } from "./signaling";
export interface WhepDependencies { origin: string; createPeer: (servers: RTCIceServer[]) => RTCPeerConnection; createStream: () => MediaStream; fetch: typeof fetch; supportsCodecs: () => boolean; timeoutMs: number }
export type WhepStage = "setup" | "options" | "offer" | "gathering" | "post" | "answer" | "connection" | "connected";
export type WhepFailure = "stream-unavailable" | "permission" | "http-response" | "network-or-timeout" | "candidate-timeout" | "connection-failed" | "unsupported-codecs" | "invalid-response" | "cancelled";
export interface WhepDiagnostic {
  at: string; outcome: "connected" | "failed" | "cancelled"; stage: WhepStage;
  failure: WhepFailure | null;
  elapsedMs: number; timingsMs: Partial<Record<WhepStage, number>>; optionsStatus: number | null; postStatus: number | null;
  gatheringState: RTCIceGatheringState | null; iceState: RTCIceConnectionState | null; connectionState: RTCPeerConnectionState | null;
  candidates: { host: number; srflx: number; prflx: number; relay: number }; grantRemainingMs: number | null;
}
// MediaMTX 1.21.2 adds a PCMU placeholder to video-only streams. An Opus-only
// offer rejects that sender and fails the entire WHEP POST.
const acceptedCodecs: Record<"audio" | "video", readonly string[]> = { audio: ["audio/opus", "audio/pcmu"], video: ["video/h264", "video/rtx", "video/red", "video/ulpfec"] };
const canceled = () => new DOMException("Playback canceled", "AbortError");
const defaultDependencies = (): WhepDependencies => ({
  origin: location.origin, createPeer: servers => new RTCPeerConnection({ iceServers: servers }), createStream: () => new MediaStream(), fetch: globalThis.fetch.bind(globalThis), timeoutMs: 15000,
  supportsCodecs: () => supportsCodec("receive", "video", "video/h264") && supportsCodec("receive", "audio", "audio/opus"),
});
export class WhepSession {
  private readonly deps: WhepDependencies;
  private readonly abort = new AbortController();
  private peer: RTCPeerConnection | null = null;
  private stream: MediaStream | null = null;
  private resource: OwnedResource | null = null;
  private started = false;
  private connected = false;
  private externalSignal: AbortSignal | null = null;
  private unsupervise: (() => void) | null = null;
  private diagnostic: WhepDiagnostic = { at: "", outcome: "failed", stage: "setup", failure: null, elapsedMs: 0, timingsMs: {}, optionsStatus: null, postStatus: null, gatheringState: null, iceState: null, connectionState: null, candidates: { host: 0, srflx: 0, prflx: 0, relay: 0 }, grantRemainingMs: null };
  private startedAt = 0;
  private stageAt = 0;
  private readonly externalAbort = () => { void this.close(); };
  private readonly track = (event: RTCTrackEvent) => {
    if (this.abort.signal.aborted) { event.track.stop(); return; }
    if (!this.stream?.getTracks().some(track => track.id === event.track.id)) this.stream?.addTrack(event.track);
  };
  constructor(deps?: WhepDependencies, private readonly onFailure?: (error: Error) => void, private readonly onDiagnostic?: (sample: WhepDiagnostic) => void) {
    this.deps = deps ?? defaultDependencies();
  }
  async start(grant: PlaybackGrant, signal: AbortSignal): Promise<MediaStream> {
    if (this.started) throw new Error("A media session has one owner and one start; close it before reconnecting.");
    this.started = true;
    this.startedAt = this.stageAt = performance.now();
    this.externalSignal = signal; signal.addEventListener("abort", this.externalAbort, { once: true });
    try {
      if (signal.aborted || this.abort.signal.aborted) throw canceled();
      if (!this.deps.supportsCodecs()) { this.diagnostic.failure = "unsupported-codecs"; throw new Error("This browser cannot receive H.264 video and Opus audio. Use an up-to-date Chrome, Edge, Firefox or Safari."); }
      const endpoint = new URL(grant.whepUri, this.deps.origin);
      if (!isSignalingEndpoint(endpoint, this.deps.origin, grant.path, "whep")) throw new Error("Unexpected playback endpoint.");
      this.stage("options");
      const options = await this.request(endpoint, { method: "OPTIONS", headers: { Authorization: `Bearer ${grant.token}` } });
      this.diagnostic.optionsStatus = options.status;
      if (!options.ok) throw new Error(`Playback permission or server connection failed (${options.status}). Rejoin or retry with a fresh grant.`);
      this.stage("offer");
      const peer = this.peer = this.deps.createPeer(parseIceServers(options.headers.get("Link"))); this.stream = this.deps.createStream();
      peer.addEventListener("track", this.track);
      this.unsupervise = superviseConnection(peer, () => this.connected && !this.abort.signal.aborted, () => this.fail());
      for (const kind of ["video", "audio"] as const) {
        const transceiver = peer.addTransceiver(kind, { direction: "recvonly" });
        if (typeof RTCRtpReceiver !== "undefined" && typeof transceiver.setCodecPreferences === "function") {
          const codecs = RTCRtpReceiver.getCapabilities(kind)?.codecs.filter(codec => acceptedCodecs[kind].includes(codec.mimeType.toLowerCase()));
          if (codecs?.length) transceiver.setCodecPreferences(codecs);
        }
      }
      await peer.setLocalDescription(await peer.createOffer());
      this.stage("gathering");
      // STUN on an unreachable secondary interface can outlive the deadline even
      // when usable host/reflexive candidates already exist. Send that bounded
      // snapshot; the subsequent ICE connection check still has to succeed.
      await this.wait("icegatheringstatechange", () => this.peer?.iceGatheringState === "complete", "Candidate gathering timed out. Check relay configuration.", undefined, () => hasCandidate(this.peer));
      this.stage("post");
      const expires = Date.parse(grant.expiresAt);
      this.diagnostic.grantRemainingMs = Number.isFinite(expires) ? Math.max(0, expires - Date.now()) : null;
      const response = await this.request(endpoint, { method: "POST", headers: { Authorization: `Bearer ${grant.token}`, "Content-Type": "application/sdp" }, body: peer.localDescription?.sdp });
      this.diagnostic.postStatus = response.status;
      if (response.status === 404) throw new Error("The media server has no playable stream at this endpoint (404). The publisher may still be starting or may have stopped. See Connection diagnostics.");
      if (response.status === 401 || response.status === 403) throw new Error(`Playback permission was rejected (${response.status}). Rejoin or retry with a fresh grant.`);
      if (response.status !== 201) throw new Error(`The stream could not be opened (${response.status}). See Connection diagnostics.`);
      this.stage("answer");
      const location = response.headers.get("Location");
      if (!location) throw new Error("Stream server omitted its playback resource.");
      const resource = new URL(location, endpoint);
      if (!isMediaResource(resource, this.deps.origin)) throw new Error("Unexpected playback resource URL.");
      this.resource = { url: resource, token: grant.token };
      if (this.abort.signal.aborted || !this.peer) throw canceled();
      if (!response.headers.get("Content-Type")?.toLowerCase().startsWith("application/sdp")) throw new Error("Stream server returned an invalid SDP answer.");
      await peer.setRemoteDescription({ type: "answer", sdp: await response.text() });
      this.stage("connection");
      await this.wait("connectionstatechange", () => this.peer?.connectionState === "connected", connectionError().message, () => this.peer?.connectionState === "failed");
      this.connected = true; this.stage("connected"); this.report("connected"); return this.stream!;
    } catch (error) {
      const wasCanceled = signal.aborted || this.abort.signal.aborted;
      this.diagnostic.failure = wasCanceled ? "cancelled" : this.diagnostic.failure ?? this.classifyFailure();
      this.report(wasCanceled ? "cancelled" : "failed");
      await this.close(); throw error;
    }
  }
  private classifyFailure(): WhepFailure {
    const { stage } = this.diagnostic;
    const signaling = stage === "options" || stage === "post";
    const status = stage === "options" ? this.diagnostic.optionsStatus : this.diagnostic.postStatus;
    if (status === 401 || status === 403) return "permission";
    if (status === 404 && stage === "post") return "stream-unavailable";
    if (status !== null && signaling) return "http-response";
    if (stage === "gathering") return "candidate-timeout";
    if (stage === "connection") return "connection-failed";
    return signaling ? "network-or-timeout" : "invalid-response";
  }
  private stage(stage: WhepStage) {
    const now = performance.now();
    this.diagnostic.timingsMs[this.diagnostic.stage] = Math.round(now - this.stageAt);
    this.diagnostic.stage = stage; this.stageAt = now;
  }
  private report(outcome: WhepDiagnostic["outcome"]) {
    const now = performance.now();
    const candidates = { host: 0, srflx: 0, prflx: 0, relay: 0 };
    for (const match of (this.peer?.localDescription?.sdp ?? "").matchAll(/^a=candidate:.*\btyp (host|srflx|prflx|relay)\b/gm)) candidates[match[1] as keyof typeof candidates]++;
    // Only numeric timings, states and candidate types leave the signaling layer.
    // Never retain SDP, addresses, paths, grants or upstream response bodies.
    const sample: WhepDiagnostic = { ...this.diagnostic, at: new Date().toISOString(), outcome, elapsedMs: Math.round(now - this.startedAt),
      timingsMs: { ...this.diagnostic.timingsMs, [this.diagnostic.stage]: Math.round(now - this.stageAt) },
      gatheringState: this.peer?.iceGatheringState ?? null, iceState: this.peer?.iceConnectionState ?? null, connectionState: this.peer?.connectionState ?? null, candidates };
    try { this.onDiagnostic?.(sample); } catch { /* Observations must not interrupt media ownership/cleanup. */ }
  }
  getPeer(): RTCPeerConnection | null { return this.peer; }
  private request(url: URL, init: RequestInit): Promise<Response> {
    return signalingRequest(this.deps.fetch, url, init, this.abort.signal, this.deps.timeoutMs);
  }
  private wait(event: string, ready: () => boolean, message: string, failed?: () => boolean, acceptAtDeadline?: () => boolean): Promise<void> {
    return waitForPeer(this.peer, { event, ready, failed, acceptAtDeadline, timeoutMs: this.deps.timeoutMs, signal: this.abort.signal, failure: () => new Error(message), aborted: canceled });
  }
  private fail() { const error = connectionError(); this.stage("connection"); this.diagnostic.failure = "connection-failed"; this.report("failed"); void this.close().finally(() => this.onFailure?.(error)); }
  async close(): Promise<void> {
    this.abort.abort(); this.connected = false;
    this.unsupervise?.(); this.unsupervise = null;
    this.externalSignal?.removeEventListener("abort", this.externalAbort); this.externalSignal = null;
    const peer = this.peer; this.peer = null;
    if (peer) {
      peer.removeEventListener("track", this.track);
      for (const receiver of peer.getReceivers()) receiver.track?.stop();
      peer.close();
    }
    for (const track of this.stream?.getTracks() ?? []) track.stop(); this.stream = null;
    const resource = this.resource; this.resource = null;
    await deleteResource(this.deps.fetch, resource);
  }
}
function connectionError(): Error { return new Error("Stream connection failed. Check server UDP reachability, firewall, or relay settings."); }
