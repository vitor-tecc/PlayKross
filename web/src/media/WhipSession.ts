import type { PublishReservation } from "../api/contracts";
import { parseIceServers } from "./WhepSession";
export interface PublicationTransport { start(grant: PublishReservation, stream: MediaStream, signal: AbortSignal): Promise<void>; close(): Promise<void> }
export interface WhipDependencies { origin: string; fetch: typeof fetch; createPeer: (servers: RTCIceServer[]) => RTCPeerConnection; supportsCodecs: (audio: boolean) => boolean; timeoutMs: number }
export class WhipSession implements PublicationTransport {
  private readonly deps: WhipDependencies;
  private readonly abort = new AbortController();
  private peer: RTCPeerConnection | null = null;
  private resource: { url: URL; token: string } | null = null;
  private started = false;
  private connected = false;
  private external: AbortSignal | null = null;
  private disconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly externalAbort = () => { void this.close(); };
  private readonly connectionChanged = () => {
    if (this.disconnectTimer) clearTimeout(this.disconnectTimer);
    this.disconnectTimer = null;
    if (!this.connected || this.abort.signal.aborted) return;
    if (this.peer?.connectionState === "failed") this.fail();
    else if (this.peer?.connectionState === "disconnected") this.disconnectTimer = setTimeout(() => this.fail(), 2000);
  };
  constructor(deps?: WhipDependencies, private readonly onFailure?: (error: Error) => void) {
    this.deps = deps ?? { origin: location.origin, fetch: globalThis.fetch.bind(globalThis), createPeer: servers => new RTCPeerConnection({ iceServers: servers }), timeoutMs: 15000,
      supportsCodecs: audio => typeof RTCRtpSender !== "undefined" && Boolean(RTCRtpSender.getCapabilities("video")?.codecs.some(c => c.mimeType.toLowerCase() === "video/h264")) && (!audio || Boolean(RTCRtpSender.getCapabilities("audio")?.codecs.some(c => c.mimeType.toLowerCase() === "audio/opus"))) };
  }
  async start(grant: PublishReservation, stream: MediaStream, signal: AbortSignal): Promise<void> {
    if (this.started) throw broadcastError();
    this.started = true; this.external = signal; signal.addEventListener("abort", this.externalAbort, { once: true });
    try {
      this.check(signal);
      const video = stream.getVideoTracks(), audio = stream.getAudioTracks();
      if (video.length !== 1 || video[0]!.readyState !== "live" || audio.length > 1 || !this.deps.supportsCodecs(audio.length > 0)) throw broadcastError();
      const expires = Date.parse(grant.expiresAt);
      const endpoint = new URL(grant.whipUri, this.deps.origin);
      if (!Number.isFinite(expires) || expires <= Date.now() || !/^rooms\/[a-f0-9]{32}\/streams\/[a-f0-9]{32}\/main$/.test(grant.path) ||
        endpoint.origin !== this.deps.origin || endpoint.pathname !== `/media/${grant.path}/whip` || endpoint.search || endpoint.hash || endpoint.username || endpoint.password) throw broadcastError();
      const options = await this.request(endpoint, { method: "OPTIONS", headers: { Authorization: `Bearer ${grant.token}` } });
      this.check(signal); if (!options.ok) throw broadcastError();
      const peer = this.deps.createPeer(parseIceServers(options.headers.get("Link"))); this.peer = peer;
      peer.addEventListener("connectionstatechange", this.connectionChanged);
      let videoSender: RTCRtpSender | null = null;
      for (const track of [...video, ...audio]) {
        const transceiver = peer.addTransceiver(track, { direction: "sendonly", streams: [stream], ...(track.kind === "video" ? { sendEncodings: [{ maxBitrate: 4_000_000, maxFramerate: 30 }] } : {}) });
        if (track.kind === "video") videoSender = transceiver.sender;
        if (typeof RTCRtpSender !== "undefined" && typeof transceiver.setCodecPreferences === "function") {
          const codecs = RTCRtpSender.getCapabilities(track.kind)?.codecs.filter(c => c.mimeType.toLowerCase() === (track.kind === "video" ? "video/h264" : "audio/opus"));
          if (codecs?.length) transceiver.setCodecPreferences(codecs);
        }
      }
      await peer.setLocalDescription(await peer.createOffer()); this.check(signal);
      await this.wait(peer, "icegatheringstatechange", () => peer.iceGatheringState === "complete", () => false,
        () => /^a=candidate:/m.test(peer.localDescription?.sdp ?? ""));
      this.check(signal); if (expires <= Date.now()) throw broadcastError();
      const response = await this.request(endpoint, { method: "POST", headers: { Authorization: `Bearer ${grant.token}`, "Content-Type": "application/sdp" }, body: peer.localDescription?.sdp });
      if (response.status !== 201) throw broadcastError();
      const location = response.headers.get("Location"); if (!location) throw broadcastError();
      const resource = new URL(location, endpoint);
      if (resource.origin !== this.deps.origin || !/^\/media-resources\/[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(resource.pathname) || resource.search || resource.hash || resource.username || resource.password) throw broadcastError();
      // Store before checking cancellation/body so late responses can be deleted.
      this.resource = { url: resource, token: grant.token }; this.check(signal);
      if (!response.headers.get("Content-Type")?.toLowerCase().startsWith("application/sdp")) throw broadcastError();
      const answer = await response.text(); this.check(signal);
      if (!compatibleAnswer(answer, audio.length > 0)) throw broadcastError();
      await peer.setRemoteDescription({ type: "answer", sdp: answer }); this.check(signal);
      const parameters = videoSender!.getParameters();
      if (parameters.encodings.length !== 1) throw broadcastError();
      parameters.encodings[0]!.maxBitrate = 4_000_000;
      await videoSender!.setParameters(parameters); this.check(signal);
      await this.wait(peer, "connectionstatechange", () => peer.connectionState === "connected", () => peer.connectionState === "failed" || peer.connectionState === "closed");
      this.check(signal); this.connected = true;
    } catch {
      await this.close();
      throw signal.aborted ? new DOMException("Broadcast canceled", "AbortError") : broadcastError();
    }
  }
  private check(signal: AbortSignal) { if (signal.aborted || this.abort.signal.aborted) throw new DOMException("Broadcast canceled", "AbortError"); }
  private request(url: URL, init: RequestInit): Promise<Response> {
    return this.deps.fetch(url, { ...init, credentials: "same-origin", signal: AbortSignal.any([this.abort.signal, AbortSignal.timeout(this.deps.timeoutMs)]) });
  }
  private wait(peer: RTCPeerConnection, event: string, ready: () => boolean, failed: () => boolean, acceptAtDeadline = () => false): Promise<void> {
    if (this.abort.signal.aborted || failed()) return Promise.reject(broadcastError());
    if (ready()) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const cleanup = () => { clearTimeout(timer); peer.removeEventListener(event, changed); this.abort.signal.removeEventListener("abort", aborted); };
      const changed = () => { if (failed()) { cleanup(); reject(broadcastError()); } else if (ready()) { cleanup(); resolve(); } };
      const aborted = () => { cleanup(); reject(broadcastError()); };
      const timer = setTimeout(() => { cleanup(); if (acceptAtDeadline()) resolve(); else reject(broadcastError()); }, this.deps.timeoutMs);
      peer.addEventListener(event, changed); this.abort.signal.addEventListener("abort", aborted, { once: true }); changed();
    });
  }
  private fail() { if (!this.connected) return; this.connected = false; void this.close().finally(() => this.onFailure?.(broadcastError())); }
  async close(): Promise<void> {
    this.abort.abort(); this.connected = false;
    if (this.disconnectTimer) clearTimeout(this.disconnectTimer); this.disconnectTimer = null;
    this.external?.removeEventListener("abort", this.externalAbort); this.external = null;
    const peer = this.peer; this.peer = null;
    if (peer) { peer.removeEventListener("connectionstatechange", this.connectionChanged); peer.close(); }
    const resource = this.resource; this.resource = null;
    if (resource) {
      try { await this.deps.fetch(resource.url, { method: "DELETE", credentials: "same-origin", headers: { Authorization: `Bearer ${resource.token}` }, keepalive: true, signal: AbortSignal.timeout(5000) }); }
      catch { /* Lease/revocation remains fallback cleanup after network loss. */ }
    }
  }
}
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
