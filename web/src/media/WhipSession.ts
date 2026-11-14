import type { PublishReservation } from "../api/contracts";
import { deleteResource, hasCandidate, isMediaResource, isSignalingEndpoint, parseIceServers, signalingRequest, superviseConnection, supportsCodec, waitForPeer, type OwnedResource } from "./signaling";
import { defaultQuality, validQuality, type BroadcastQuality } from "./BroadcastQuality";
export interface BroadcastStats { bitrateMbps: number | null; width: number | null; height: number | null; fps: number | null; limitation: "none" | "cpu" | "bandwidth" | "other" | null }
export interface PublicationTransport { start(grant: PublishReservation, stream: MediaStream, signal: AbortSignal, quality?: BroadcastQuality): Promise<void>; readStats?(): Promise<BroadcastStats | null>; close(): Promise<void> }
export interface WhipDependencies { origin: string; fetch: typeof fetch; createPeer: (servers: RTCIceServer[]) => RTCPeerConnection; supportsCodecs: (audio: boolean) => boolean; timeoutMs: number }
const publishPath = /^rooms\/[a-f0-9]{32}\/streams\/[a-f0-9]{32}\/main$/;
const canceled = () => new DOMException("Broadcast canceled", "AbortError");
const defaultDependencies = (): WhipDependencies => ({
  origin: location.origin, fetch: globalThis.fetch.bind(globalThis), createPeer: servers => new RTCPeerConnection({ iceServers: servers }), timeoutMs: 15000,
  supportsCodecs: audio => supportsCodec("send", "video", "video/h264") && (!audio || supportsCodec("send", "audio", "audio/opus")),
});
export class WhipSession implements PublicationTransport {
  private readonly deps: WhipDependencies;
  private readonly abort = new AbortController();
  private peer: RTCPeerConnection | null = null;
  private resource: OwnedResource | null = null;
  private started = false;
  private connected = false;
  private previousStats: { id: string; timestamp: number; bytes: number } | null = null;
  private external: AbortSignal | null = null;
  private unsupervise: (() => void) | null = null;
  private readonly externalAbort = () => { void this.close(); };
  constructor(deps?: WhipDependencies, private readonly onFailure?: (error: Error) => void) {
    this.deps = deps ?? defaultDependencies();
  }
  async start(grant: PublishReservation, stream: MediaStream, signal: AbortSignal, quality: BroadcastQuality = defaultQuality): Promise<void> {
    if (this.started) throw broadcastError();
    this.started = true; this.external = signal; signal.addEventListener("abort", this.externalAbort, { once: true });
    try {
      this.check(signal);
      if (!validQuality(quality)) throw broadcastError();
      const video = stream.getVideoTracks(), audio = stream.getAudioTracks();
      if (video.length !== 1 || video[0]!.readyState !== "live" || audio.length > 1 || !this.deps.supportsCodecs(audio.length > 0)) throw broadcastError();
      const expires = Date.parse(grant.expiresAt);
      const endpoint = new URL(grant.whipUri, this.deps.origin);
      if (!Number.isFinite(expires) || expires <= Date.now() || !publishPath.test(grant.path) || !isSignalingEndpoint(endpoint, this.deps.origin, grant.path, "whip")) throw broadcastError();
      const options = await this.request(endpoint, { method: "OPTIONS", headers: { Authorization: `Bearer ${grant.token}` } });
      this.check(signal); if (!options.ok) throw broadcastError();
      const peer = this.deps.createPeer(parseIceServers(options.headers.get("Link"))); this.peer = peer;
      this.unsupervise = superviseConnection(peer, () => this.connected && !this.abort.signal.aborted, () => this.fail());
      const encoding = { maxBitrate: quality.bitrateMbps * 1_000_000, maxFramerate: quality.fps };
      let videoSender: RTCRtpSender | null = null;
      for (const track of [...video, ...audio]) {
        const transceiver = peer.addTransceiver(track, { direction: "sendonly", streams: [stream], ...(track.kind === "video" ? { sendEncodings: [{ ...encoding }] } : {}) });
        if (track.kind === "video") videoSender = transceiver.sender;
        if (typeof RTCRtpSender !== "undefined" && typeof transceiver.setCodecPreferences === "function") {
          const wanted = track.kind === "video" ? "video/h264" : "audio/opus";
          const codecs = RTCRtpSender.getCapabilities(track.kind)?.codecs.filter(c => c.mimeType.toLowerCase() === wanted);
          if (codecs?.length) transceiver.setCodecPreferences(codecs);
        }
      }
      await peer.setLocalDescription(await peer.createOffer()); this.check(signal);
      await this.wait(peer, "icegatheringstatechange", () => peer.iceGatheringState === "complete", undefined, () => hasCandidate(peer));
      this.check(signal); if (expires <= Date.now()) throw broadcastError();
      const response = await this.request(endpoint, { method: "POST", headers: { Authorization: `Bearer ${grant.token}`, "Content-Type": "application/sdp" }, body: peer.localDescription?.sdp });
      if (response.status !== 201) throw broadcastError();
      const location = response.headers.get("Location"); if (!location) throw broadcastError();
      const resource = new URL(location, endpoint);
      if (!isMediaResource(resource, this.deps.origin)) throw broadcastError();
      // Store before checking cancellation/body so late responses can be deleted.
      this.resource = { url: resource, token: grant.token }; this.check(signal);
      if (!response.headers.get("Content-Type")?.toLowerCase().startsWith("application/sdp")) throw broadcastError();
      const answer = await response.text(); this.check(signal);
      if (!compatibleAnswer(answer, audio.length > 0)) throw broadcastError();
      await peer.setRemoteDescription({ type: "answer", sdp: answer }); this.check(signal);
      // Some browsers drop sendEncodings limits during negotiation; re-apply them.
      const parameters = videoSender!.getParameters();
      if (parameters.encodings.length !== 1) throw broadcastError();
      Object.assign(parameters.encodings[0]!, encoding);
      await videoSender!.setParameters(parameters); this.check(signal);
      await this.wait(peer, "connectionstatechange", () => peer.connectionState === "connected", () => peer.connectionState === "failed" || peer.connectionState === "closed");
      this.check(signal); this.connected = true;
    } catch {
      await this.close();
      throw signal.aborted ? canceled() : broadcastError();
    }
  }
  private check(signal: AbortSignal) { if (signal.aborted || this.abort.signal.aborted) throw canceled(); }
  private request(url: URL, init: RequestInit): Promise<Response> {
    return signalingRequest(this.deps.fetch, url, init, this.abort.signal, this.deps.timeoutMs);
  }
  private wait(peer: RTCPeerConnection, event: string, ready: () => boolean, failed?: () => boolean, acceptAtDeadline?: () => boolean): Promise<void> {
    return waitForPeer(peer, { event, ready, failed, acceptAtDeadline, timeoutMs: this.deps.timeoutMs, signal: this.abort.signal, failure: broadcastError, aborted: broadcastError });
  }
  private fail() { if (!this.connected) return; this.connected = false; void this.close().finally(() => this.onFailure?.(broadcastError())); }
  async readStats(): Promise<BroadcastStats | null> {
    const peer = this.peer;
    if (!peer || !this.connected) return null;
    const report = await peer.getStats();
    if (this.peer !== peer || !this.connected) return null;
    let video: RTCOutboundRtpStreamStats | undefined;
    report.forEach(stat => { if (stat.type === "outbound-rtp" && (stat.kind ?? stat.mediaType) === "video" && !stat.isRemote) video = stat; });
    if (!video) { this.previousStats = null; return null; }
    const previous = this.previousStats;
    const bytes = finiteStat(video.bytesSent), timestamp = finiteStat(video.timestamp);
    const bitrateMbps = previous && previous.id === video.id && bytes !== null && timestamp !== null && timestamp > previous.timestamp && bytes >= previous.bytes
      ? (bytes - previous.bytes) * 8 / (timestamp - previous.timestamp) / 1000 : null;
    this.previousStats = bytes !== null && timestamp !== null ? { id: video.id, timestamp, bytes } : null;
    const reason = video.qualityLimitationReason;
    return { bitrateMbps, width: finiteStat(video.frameWidth), height: finiteStat(video.frameHeight), fps: finiteStat(video.framesPerSecond),
      limitation: reason === "none" || reason === "cpu" || reason === "bandwidth" || reason === "other" ? reason : null };
  }
  async close(): Promise<void> {
    this.abort.abort(); this.connected = false;
    this.previousStats = null;
    this.unsupervise?.(); this.unsupervise = null;
    this.external?.removeEventListener("abort", this.externalAbort); this.external = null;
    const peer = this.peer; this.peer = null;
    peer?.close();
    const resource = this.resource; this.resource = null;
    await deleteResource(this.deps.fetch, resource);
  }
}
function finiteStat(value: number | undefined): number | null { return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : null; }
function broadcastError() { return new Error("Browser broadcast failed. Check H.264/Opus support, room permission, UDP/firewall/relay settings and retry sharing."); }
function compatibleAnswer(sdp: string, audio: boolean): boolean {
  if (!/^v=0\r?\n/.test(sdp)) return false;
  const sections = sdp.split(/(?=^m=)/m);
  return ["video", ...(audio ? ["audio"] : [])].every(kind => {
    const section = sections.find(s => s.startsWith(`m=${kind} `));
    const media = section && /^m=\S+\s+(\d+)\s+\S+\s+([^\r\n]+)/.exec(section);
    if (!section || !media || media[1] === "0" || /^a=(inactive|sendonly)\r?$/m.test(section)) return false;
    const codec = kind === "video" ? "H264" : "opus";
    return media[2]!.trim().split(/\s+/).some(payload => new RegExp(`^a=rtpmap:${payload} ${codec}/`, "im").test(section));
  });
}
