import type { PlaybackGrant } from "../api/contracts";
export interface WhepDependencies { origin: string; createPeer: (servers: RTCIceServer[]) => RTCPeerConnection; createStream: () => MediaStream; fetch: typeof fetch; supportsCodecs: () => boolean; timeoutMs: number }
export type WhepStage = "setup" | "options" | "offer" | "gathering" | "post" | "answer" | "connection" | "connected";
export interface WhepDiagnostic {
  at: string; outcome: "connected" | "failed" | "cancelled"; stage: WhepStage;
  failure: "stream-unavailable" | "permission" | "http-response" | "network-or-timeout" | "candidate-timeout" | "connection-failed" | "unsupported-codecs" | "invalid-response" | "cancelled" | null;
  elapsedMs: number; timingsMs: Partial<Record<WhepStage, number>>; optionsStatus: number | null; postStatus: number | null;
  gatheringState: RTCIceGatheringState | null; iceState: RTCIceConnectionState | null; connectionState: RTCPeerConnectionState | null;
  candidates: { host: number; srflx: number; prflx: number; relay: number }; grantRemainingMs: number | null;
}
export class WhepSession {
  private readonly deps: WhepDependencies;
  private readonly abort = new AbortController();
  private peer: RTCPeerConnection | null = null;
  private stream: MediaStream | null = null;
  private resource: { url: URL; token: string } | null = null;
  private started = false;
  private connected = false;
  private externalSignal: AbortSignal | null = null;
  private disconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private diagnostic: WhepDiagnostic = { at: "", outcome: "failed", stage: "setup", failure: null, elapsedMs: 0, timingsMs: {}, optionsStatus: null, postStatus: null, gatheringState: null, iceState: null, connectionState: null, candidates: { host: 0, srflx: 0, prflx: 0, relay: 0 }, grantRemainingMs: null };
  private startedAt = 0;
  private stageAt = 0;
  private readonly externalAbort = () => { void this.close(); };
  private readonly track = (event: RTCTrackEvent) => {
    if (this.abort.signal.aborted) { event.track.stop(); return; }
    if (!this.stream?.getTracks().some(track => track.id === event.track.id)) this.stream?.addTrack(event.track);
  };
  private readonly connectionChanged = () => {
    if (this.disconnectTimer) { clearTimeout(this.disconnectTimer); this.disconnectTimer = null; }
    if (!this.connected || this.abort.signal.aborted) return;
    if (this.peer?.connectionState === "failed") this.fail();
    else if (this.peer?.connectionState === "disconnected") this.disconnectTimer = setTimeout(() => this.fail(), 2000);
  };
  constructor(deps?: WhepDependencies, private readonly onFailure?: (error: Error) => void, private readonly onDiagnostic?: (sample: WhepDiagnostic) => void) {
    this.deps = deps ?? {
      origin: location.origin, createPeer: servers => new RTCPeerConnection({ iceServers: servers }), createStream: () => new MediaStream(), fetch: globalThis.fetch.bind(globalThis), timeoutMs: 15000,
      supportsCodecs: () => typeof RTCRtpReceiver !== "undefined" && Boolean(RTCRtpReceiver.getCapabilities("video")?.codecs.some(codec => codec.mimeType.toLowerCase() === "video/h264")) && Boolean(RTCRtpReceiver.getCapabilities("audio")?.codecs.some(codec => codec.mimeType.toLowerCase() === "audio/opus")),
    };
  }
  async start(grant: PlaybackGrant, signal: AbortSignal): Promise<MediaStream> {
    if (this.started) throw new Error("A media session has one owner and one start; close it before reconnecting.");
    this.started = true;
    this.startedAt = this.stageAt = performance.now();
    this.externalSignal = signal; signal.addEventListener("abort", this.externalAbort, { once: true });
    try {
      if (signal.aborted || this.abort.signal.aborted) throw new DOMException("Playback canceled", "AbortError");
      if (!this.deps.supportsCodecs()) { this.diagnostic.failure = "unsupported-codecs"; throw new Error("This browser cannot receive H.264 video and Opus audio. Use an up-to-date Chrome, Edge, Firefox or Safari."); }
      const endpoint = new URL(grant.whepUri, this.deps.origin);
      if (endpoint.origin !== this.deps.origin || endpoint.pathname !== `/media/${grant.path}/whep` || endpoint.search || endpoint.hash || endpoint.username || endpoint.password) throw new Error("Unexpected playback endpoint.");
      this.stage("options");
      const options = await this.request(endpoint, { method: "OPTIONS", headers: { Authorization: `Bearer ${grant.token}` } });
      this.diagnostic.optionsStatus = options.status;
      if (!options.ok) throw new Error(`Playback permission or server connection failed (${options.status}). Rejoin or retry with a fresh grant.`);
      const servers = parseIceServers(options.headers.get("Link"));
      this.stage("offer");
      this.peer = this.deps.createPeer(servers); this.stream = this.deps.createStream();
      this.peer.addEventListener("track", this.track); this.peer.addEventListener("connectionstatechange", this.connectionChanged);
      for (const kind of ["video", "audio"] as const) {
        const transceiver = this.peer.addTransceiver(kind, { direction: "recvonly" });
        if (typeof RTCRtpReceiver !== "undefined" && typeof transceiver.setCodecPreferences === "function") {
          const codecs = RTCRtpReceiver.getCapabilities(kind)?.codecs.filter(codec => kind === "audio" ? codec.mimeType.toLowerCase() === "audio/opus" : ["video/h264", "video/rtx", "video/red", "video/ulpfec"].includes(codec.mimeType.toLowerCase()));
          if (codecs?.length) transceiver.setCodecPreferences(codecs);
        }
      }
      await this.peer.setLocalDescription(await this.peer.createOffer());
      this.stage("gathering");
      // STUN on an unreachable secondary interface can outlive the deadline even
      // when usable host/reflexive candidates already exist. Send that bounded
      // snapshot; the subsequent ICE connection check still has to succeed.
      await this.wait("icegatheringstatechange", () => this.peer?.iceGatheringState === "complete", "Candidate gathering timed out. Check relay configuration.", () => false,
        () => /^a=candidate:/m.test(this.peer?.localDescription?.sdp ?? ""));
      this.stage("post");
      const expires = Date.parse(grant.expiresAt);
      this.diagnostic.grantRemainingMs = Number.isFinite(expires) ? Math.max(0, expires - Date.now()) : null;
      const response = await this.request(endpoint, { method: "POST", headers: { Authorization: `Bearer ${grant.token}`, "Content-Type": "application/sdp" }, body: this.peer.localDescription?.sdp });
      this.diagnostic.postStatus = response.status;
      if (response.status === 404) throw new Error("The media server has no playable stream at this endpoint (404). The publisher may still be starting or may have stopped. See Connection diagnostics.");
      if (response.status === 401 || response.status === 403) throw new Error(`Playback permission was rejected (${response.status}). Rejoin or retry with a fresh grant.`);
      if (response.status !== 201) throw new Error(`The stream could not be opened (${response.status}). See Connection diagnostics.`);
      this.stage("answer");
      const location = response.headers.get("Location");
      if (!location) throw new Error("Stream server omitted its playback resource.");
      const resource = new URL(location, endpoint);
      if (resource.origin !== this.deps.origin || !/^\/media-resources\/[0-9a-f-]{36}$/i.test(resource.pathname) || resource.search || resource.hash || resource.username || resource.password) throw new Error("Unexpected playback resource URL.");
      this.resource = { url: resource, token: grant.token };
      if (this.abort.signal.aborted || !this.peer) throw new DOMException("Playback canceled", "AbortError");
      if (!response.headers.get("Content-Type")?.toLowerCase().startsWith("application/sdp")) throw new Error("Stream server returned an invalid SDP answer.");
      await this.peer.setRemoteDescription({ type: "answer", sdp: await response.text() });
      this.stage("connection");
      await this.wait("connectionstatechange", () => this.peer?.connectionState === "connected", connectionError().message, () => this.peer?.connectionState === "failed");
      this.connected = true; this.stage("connected"); this.report("connected"); return this.stream;
    } catch (error) {
      const canceled = signal.aborted || this.abort.signal.aborted;
      const status = this.diagnostic.stage === "options" ? this.diagnostic.optionsStatus : this.diagnostic.postStatus;
      this.diagnostic.failure = canceled ? "cancelled" : this.diagnostic.failure ??
        (status === 401 || status === 403 ? "permission" : status === 404 && this.diagnostic.stage === "post" ? "stream-unavailable" :
        status !== null && (this.diagnostic.stage === "options" || this.diagnostic.stage === "post") ? "http-response" :
        this.diagnostic.stage === "gathering" ? "candidate-timeout" : this.diagnostic.stage === "connection" ? "connection-failed" :
        this.diagnostic.stage === "options" || this.diagnostic.stage === "post" ? "network-or-timeout" : "invalid-response");
      this.report(canceled ? "cancelled" : "failed");
      await this.close(); throw error;
    }
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
    return this.deps.fetch(url, { ...init, credentials: "same-origin", signal: AbortSignal.any([this.abort.signal, AbortSignal.timeout(this.deps.timeoutMs)]) });
  }
  private wait(event: string, ready: () => boolean, message: string, failed: () => boolean = () => false, acceptAtDeadline: () => boolean = () => false): Promise<void> {
    const peer = this.peer;
    if (!peer || this.abort.signal.aborted) return Promise.reject(new DOMException("Playback canceled", "AbortError"));
    if (failed()) return Promise.reject(new Error(message));
    if (ready()) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const cleanup = () => { clearTimeout(timer); peer.removeEventListener(event, changed); this.abort.signal.removeEventListener("abort", aborted); };
      const changed = () => { if (failed()) { cleanup(); reject(new Error(message)); } else if (ready()) { cleanup(); resolve(); } };
      const aborted = () => { cleanup(); reject(new DOMException("Playback canceled", "AbortError")); };
      const timer = setTimeout(() => { cleanup(); if (acceptAtDeadline()) resolve(); else reject(new Error(message)); }, this.deps.timeoutMs);
      peer.addEventListener(event, changed); this.abort.signal.addEventListener("abort", aborted, { once: true });
      changed();
    });
  }
  private fail() { const error = connectionError(); this.stage("connection"); this.diagnostic.failure = "connection-failed"; this.report("failed"); void this.close().finally(() => this.onFailure?.(error)); }
  async close(): Promise<void> {
    this.abort.abort(); this.connected = false;
    if (this.disconnectTimer) { clearTimeout(this.disconnectTimer); this.disconnectTimer = null; }
    this.externalSignal?.removeEventListener("abort", this.externalAbort); this.externalSignal = null;
    const peer = this.peer; this.peer = null;
    if (peer) {
      peer.removeEventListener("track", this.track); peer.removeEventListener("connectionstatechange", this.connectionChanged);
      for (const receiver of peer.getReceivers()) receiver.track?.stop();
      peer.close();
    }
    for (const track of this.stream?.getTracks() ?? []) track.stop(); this.stream = null;
    const resource = this.resource; this.resource = null;
    if (resource) {
      try { await this.deps.fetch(resource.url, { method: "DELETE", credentials: "same-origin", headers: { Authorization: `Bearer ${resource.token}` }, signal: AbortSignal.timeout(5000), keepalive: true }); }
      catch { /* Server ICE/session expiry and explicit revocation remain fallback cleanup. */ }
    }
  }
}
function connectionError(): Error { return new Error("Stream connection failed. Check server UDP reachability, firewall, or relay settings."); }
export function parseIceServers(link: string | null): RTCIceServer[] {
  if (!link) return [];
  const servers: RTCIceServer[] = [];
  for (const match of link.matchAll(/<((?:stun|stuns|turn|turns):[^>]+)>((?:[^,<]|"[^"]*")*)/g)) {
    if (!/rel="ice-server"/.test(match[2] ?? "")) continue;
    const username = /username="([^"\\]*)"/.exec(match[2] ?? "")?.[1];
    const credential = /credential="([^"\\]*)"/.exec(match[2] ?? "")?.[1];
    servers.push({ urls: match[1]!, ...(username !== undefined ? { username } : {}), ...(credential !== undefined ? { credential } : {}) });
  }
  return servers;
}
