import type { PublishReservation, RoomSnapshot } from "../api/contracts";
import { RoomApiError, type roomApi } from "../api/roomApi";
import type { BroadcastStats, PublicationTransport } from "./WhipSession";
import { BroadcastFailure, codecFailureMessage } from "./broadcastFailure";
import { constrainCapture, defaultQuality, validQuality, type BroadcastQuality } from "./BroadcastQuality";
export type PublisherPhase = "idle" | "choosing" | "starting" | "live" | "stopping";
export interface PublisherSnapshot { phase: PublisherPhase; streamId: string | null; hasAudio: boolean; error: string | null; stats?: BroadcastStats | null }
export type PublicationApi = Pick<typeof roomApi, "reservePublication" | "renewPublication" | "stopPublication">;
export interface BrowserPublisherDependencies {
  secureContext: boolean; capture: ((options: DisplayMediaStreamOptions) => Promise<MediaStream>) | undefined; api: PublicationApi;
  createTransport: (onFailure: (error: Error) => void) => PublicationTransport;
  setCaptureAudioBlocked: (blocked: boolean) => void; now: () => number; sessionExpiresAt: string;
}
interface Attempt {
  abort: AbortController; stream: MediaStream | null; grant: PublishReservation | null; transport: PublicationTransport | null;
  handlers: Map<MediaStreamTrack, () => void>; renewal: ReturnType<typeof setTimeout> | null; cutoff: ReturnType<typeof setTimeout> | null;
  deadline: number; observed: boolean; observedBeforeGrant: Set<string>;
  statsTimer?: ReturnType<typeof setTimeout>;
  /** True once stats showed encoded video leaving the browser. */
  sentVideo?: boolean;
  statsSamples?: number;
}
export class BrowserPublisher {
  private current: Attempt | null = null;
  private stopping: Promise<void> | null = null;
  private snapshot: PublisherSnapshot = { phase: "idle", streamId: null, hasAudio: false, error: null };
  private revision = -1;
  private latestRoom: RoomSnapshot | null = null;
  constructor(private readonly deps: BrowserPublisherDependencies, private readonly changed: (state: PublisherSnapshot) => void) {}
  private update(value: Partial<PublisherSnapshot>) { this.snapshot = { ...this.snapshot, ...value }; this.changed({ ...this.snapshot }); }
  private valid(a: Attempt): boolean { return this.current === a && !a.abort.signal.aborted; }
  async start(shareAudio: boolean, requested: BroadcastQuality = defaultQuality): Promise<void> {
    if (this.current || this.stopping) return;
    const quality = { ...requested };
    if (!validQuality(quality)) {
      this.update({ error: "Choose 480p, 720p or 1080p, 30 or 60 FPS, and 1–25 Mbps in 0.5 Mbps steps." }); return;
    }
    const expires = Date.parse(this.deps.sessionExpiresAt);
    if (!this.deps.secureContext || !this.deps.capture || !Number.isFinite(expires) || expires <= Date.now()) {
      this.update({ phase: "idle", error: "Screen sharing requires a supported browser, HTTPS (or localhost) and an active room session." }); return;
    }
    const a: Attempt = { abort: new AbortController(), stream: null, grant: null, transport: null, handlers: new Map(), renewal: null, cutoff: null, deadline: Infinity, observed: false, observedBeforeGrant: new Set() };
    this.current = a; this.update({ phase: "choosing", error: null, streamId: null, hasAudio: false, stats: null });
    try {
      // This callback must synchronously mute mounted room players before the
      // chooser opens; never await network or a React effect before capture.
      if (shareAudio) this.deps.setCaptureAudioBlocked(true);
      const stream = await this.deps.capture({ video: true, audio: shareAudio });
      if (!this.valid(a)) { stopTracks(stream); return; }
      a.stream = stream;
      const video = stream.getVideoTracks()[0];
      if (!video || video.readyState !== "live") throw new Error("capture");
      const audio = shareAudio ? stream.getAudioTracks().find(t => t.readyState === "live") : undefined;
      for (const track of stream.getTracks()) if (track !== video && track !== audio) { track.stop(); stream.removeTrack(track); }
      const listen = (track: MediaStreamTrack, ended: () => void) => { a.handlers.set(track, ended); track.addEventListener("ended", ended); };
      listen(video, () => { if (this.valid(a)) void this.finish(a); });
      if (audio) listen(audio, () => {
        if (!this.valid(a)) return;
        stream.removeTrack(audio); this.update({ hasAudio: false }); this.deps.setCaptureAudioBlocked(false);
      });
      this.update({ hasAudio: Boolean(audio) });
      if (!audio && shareAudio) this.deps.setCaptureAudioBlocked(false);
      await constrainCapture(video, quality);
      if (!this.valid(a)) return;
      this.checkSession(); this.update({ phase: "starting" });
      const reservedAt = this.deps.now();
      const grant = await this.deps.api.reservePublication(AbortSignal.any([a.abort.signal, AbortSignal.timeout(15000)]));
      if (!this.valid(a)) { await this.releaseReservation(grant); return; }
      a.grant = grant; a.deadline = reservedAt + 50000; this.update({ streamId: grant.streamId });
      // The host broadcasts creation before replying to reservePublication.
      // Recover that observation only after a successful create-only grant
      // identifies our stream; never adopt an ID merely from a room snapshot.
      a.observed = a.observedBeforeGrant.has(grant.streamId) || Boolean(this.latestRoom?.streams.some(s => s.streamId === grant.streamId));
      a.observedBeforeGrant.clear();
      if (a.observed && this.latestRoom && !this.latestRoom.streams.some(s => s.streamId === grant.streamId)) {
        await this.finish(a, "The publication was removed. Start sharing again."); return;
      }
      this.scheduleCutoff(a); this.scheduleRenewal(a);
      if (!this.valid(a)) return;
      // A connection that never carried video usually means the browser has no
      // working H.264 encoder (e.g. Firefox without OpenH264); the server then drops it.
      a.transport = this.deps.createTransport(() => { if (this.valid(a)) void this.finish(a, (a.statsSamples ?? 0) >= 2 && !a.sentVideo ? codecFailureMessage : "The broadcast connection ended. Start sharing again to reconnect."); });
      await a.transport.start(grant, stream, a.abort.signal, quality);
      if (!this.valid(a)) return;
      if (this.deps.now() >= a.deadline) { await this.finish(a, "Publication lease expired. Start sharing again."); return; }
      this.checkSession(); this.update({ phase: "live" });
      this.scheduleStats(a);
    } catch (error) {
      if (!this.valid(a)) return;
      const message = error instanceof BroadcastFailure ? error.message : error instanceof RoomApiError && error.status === 409 ? "This session already has a broadcast in another tab or client. Stop it there before sharing here." :
        error instanceof DOMException && error.name === "NotAllowedError" ? "Screen sharing was canceled or permission was denied. Start sharing to try again." :
        "Screen sharing could not start. Check capture permission, browser H.264/Opus support and the room connection, then retry.";
      await this.finish(a, message);
    }
  }
  private checkSession() { if (Date.parse(this.deps.sessionExpiresAt) <= Date.now()) throw new Error("session expired"); }
  private scheduleStats(a: Attempt) {
    if (!a.transport?.readStats || !this.valid(a)) return;
    a.statsTimer = setTimeout(async () => {
      a.statsTimer = undefined;
      try {
        const stats = await a.transport!.readStats!();
        if (this.valid(a)) { a.statsSamples = (a.statsSamples ?? 0) + 1; if (stats?.bitrateMbps) a.sentVideo = true; this.update({ stats }); }
      } catch { if (this.valid(a)) this.update({ stats: null }); }
      if (this.valid(a)) this.scheduleStats(a);
    }, 2000);
  }
  private scheduleCutoff(a: Attempt) {
    if (a.cutoff) clearTimeout(a.cutoff);
    const remaining = Math.min(a.deadline - this.deps.now(), Date.parse(this.deps.sessionExpiresAt) - Date.now());
    if (remaining <= 0) { void this.finish(a, "Publication lease or room session expired. Rejoin or start sharing again."); return; }
    a.cutoff = setTimeout(() => { if (this.valid(a)) this.scheduleCutoff(a); }, remaining);
  }
  private scheduleRenewal(a: Attempt) {
    a.renewal = setTimeout(() => { void this.renew(a); }, 15000);
  }
  private async renew(a: Attempt) {
    a.renewal = null;
    if (!this.valid(a)) return;
    if (this.deps.now() >= a.deadline || Date.parse(this.deps.sessionExpiresAt) <= Date.now()) { await this.finish(a, "Publication lease expired. Start sharing again."); return; }
    const sentAt = this.deps.now();
    try {
      const room = await this.deps.api.renewPublication(a.grant!.streamId, AbortSignal.any([a.abort.signal, AbortSignal.timeout(5000)]));
      if (!this.valid(a)) return;
      // A delayed successful response cannot extend ownership after our cutoff.
      if (this.deps.now() >= a.deadline || sentAt + 50000 <= this.deps.now()) { await this.finish(a, "Publication lease expired. Start sharing again."); return; }
      a.deadline = sentAt + 50000; this.scheduleCutoff(a); this.reconcile(room);
    } catch (error) {
      if (this.valid(a) && error instanceof RoomApiError && (error.status === 401 || error.status === 403)) { await this.finish(a, "Publication ownership is no longer available. Rejoin or start sharing again."); return; }
    }
    if (this.valid(a)) this.scheduleRenewal(a);
  }
  reconcile(room: RoomSnapshot): void {
    if (room.revision <= this.revision) return; this.revision = room.revision; this.latestRoom = room;
    const a = this.current; if (!a) return;
    if (!a.grant) {
      // Retain IDs only during the bounded reservation request, not while a
      // permission chooser can remain open indefinitely. They confer no rights.
      if (this.snapshot.phase === "starting") for (const stream of room.streams) a.observedBeforeGrant.add(stream.streamId);
      return;
    }
    if (room.streams.some(s => s.streamId === a.grant!.streamId)) a.observed = true;
    else if (a.observed) void this.finish(a, "The publication was removed. Start sharing again.");
  }
  stop(): Promise<void> { return this.current ? this.finish(this.current) : this.stopping ?? Promise.resolve(); }
  private finish(a: Attempt, error: string | null = null): Promise<void> {
    if (!this.valid(a)) return this.stopping ?? Promise.resolve();
    this.current = null;
    this.update({ phase: "stopping", error });
    a.abort.abort();
    if (a.renewal) clearTimeout(a.renewal); if (a.cutoff) clearTimeout(a.cutoff);
    if (a.statsTimer) clearTimeout(a.statsTimer);
    for (const [track, ended] of a.handlers) track.removeEventListener("ended", ended); a.handlers.clear();
    if (a.stream) stopTracks(a.stream);
    this.deps.setCaptureAudioBlocked(false);
    const work = Promise.allSettled([
      Promise.resolve().then(() => a.transport?.close()),
      a.grant ? this.releaseReservation(a.grant) : Promise.resolve(),
    ]).then(() => {
      this.stopping = null; this.update({ phase: "idle", streamId: null, hasAudio: false, stats: null });
    });
    this.stopping = work; return work;
  }
  private async releaseReservation(grant: PublishReservation) {
    try { await this.deps.api.stopPublication(grant.streamId, AbortSignal.timeout(5000), true); }
    catch { /* Server lease expiry handles best-effort cleanup after departure. */ }
  }
}
function stopTracks(stream: MediaStream) { for (const track of stream.getTracks()) if (track.readyState !== "ended") track.stop(); }
