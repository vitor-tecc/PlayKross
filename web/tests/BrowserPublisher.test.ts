import { afterEach, expect, it, vi } from "vitest";
import { BrowserPublisher, type BrowserPublisherDependencies, type PublisherSnapshot } from "../src/media/BrowserPublisher";
import { RoomApiError } from "../src/api/roomApi";
import { deferred, grant, Stream, Track } from "./publisherFakes";
import type { RoomSnapshot } from "../src/api/contracts";
afterEach(() => { vi.useRealTimers(); });
const room = (revision: number, present = true): RoomSnapshot => ({ roomId: "room", revision, participants: [], streams: present ? [{ streamId: grant.streamId, ownerSessionId: "me", nickname: "Me", status: "live" }] : [] });
function setup() {
  const stream = new Stream(), events: string[] = [], states: PublisherSnapshot[] = [], failures: ((error: Error) => void)[] = [];
  const transport = { start: vi.fn(async () => {}), close: vi.fn(async () => {}) };
  const api = { reservePublication: vi.fn(async (_s: AbortSignal) => grant), renewPublication: vi.fn(async (_id: string, _s: AbortSignal) => room(2)), stopPublication: vi.fn(async (_id: string, _s: AbortSignal, _keepalive?: boolean) => room(3, false)) };
  const deps: BrowserPublisherDependencies = { secureContext: true, capture: vi.fn(async (_options: DisplayMediaStreamOptions) => { events.push("chooser"); return stream.asMedia(); }), api, createTransport: failure => { failures.push(failure); return transport; }, setCaptureAudioBlocked: blocked => events.push(blocked ? "mute" : "unmute"), now: () => performance.now(), sessionExpiresAt: "2099-01-01T00:00:00Z" };
  const publisher = new BrowserPublisher(deps, value => states.push(value));
  return { publisher, deps, stream, events, states, api, transport, failures, state: () => states.at(-1)! };
}
it("CaptureStartsSynchronouslyAndNeverReservesBeforeChooserSuccess", async () => {
  const c = setup(), chosen = deferred<MediaStream>(); c.deps.capture = vi.fn(() => { c.events.push("chooser"); return chosen.promise; });
  const start = c.publisher.start(true);
  expect(c.events).toEqual(["mute", "chooser"]); expect(c.api.reservePublication).not.toHaveBeenCalled();
  await c.publisher.stop(); chosen.resolve(c.stream.asMedia()); await start;
  expect(c.stream.tracks[0]!.stop).toHaveBeenCalledOnce(); expect(c.api.reservePublication).not.toHaveBeenCalled(); expect(c.state().phase).toBe("idle");
});
it("AudioIsOptInAndExtraTracksAreStopped", async () => {
  const c = setup(), audio = new Track("audio"), extra = new Track("video", "extra"); c.stream.tracks.push(audio, extra);
  await c.publisher.start(false);
  expect(c.deps.capture).toHaveBeenCalledWith({ video: true, audio: false });
  expect(audio.stop).toHaveBeenCalledOnce(); expect(extra.stop).toHaveBeenCalledOnce();
  expect(c.stream.getTracks()).toHaveLength(1); expect(c.state()).toMatchObject({ phase: "live", hasAudio: false });
  expect(c.stream.tracks[0]!.applyConstraints).toHaveBeenCalledWith({ width: { max: 1280 }, height: { max: 720 }, frameRate: { max: 30 } });
  await c.publisher.stop();
});
it("UnavailableAudioIsVideoOnlyAndEndedAudioDoesNotStopVideo", async () => {
  const c = setup(); await c.publisher.start(true); expect(c.state()).toMatchObject({ phase: "live", hasAudio: false }); expect(c.events.at(-1)).toBe("unmute"); await c.publisher.stop();
  const d = setup(), audio = new Track("audio"); d.stream.tracks.push(audio); await d.publisher.start(true);
  expect(d.state().hasAudio).toBe(true); audio.end(); expect(d.state()).toMatchObject({ phase: "live", hasAudio: false }); expect(d.stream.tracks[0]!.stop).not.toHaveBeenCalled(); await d.publisher.stop();
});
it("LateReservationAfterStopIsReleasedWithoutTouchingRestart", async () => {
  const c = setup(), reserved = deferred<typeof grant>(), requested = deferred<void>();
  c.api.reservePublication.mockImplementationOnce(async () => { requested.resolve(); return reserved.promise; });
  const old = c.publisher.start(false); await requested.promise; await c.publisher.stop();
  const newGrant = { ...grant, streamId: "new-stream" }; c.api.reservePublication.mockResolvedValue(newGrant);
  c.deps.capture = async () => new Stream().asMedia(); await c.publisher.start(false);
  reserved.resolve(grant); await old;
  expect(c.state()).toMatchObject({ phase: "live", streamId: "new-stream" });
  expect(c.api.stopPublication.mock.calls.map(c => c[0])).toEqual([grant.streamId]); await c.publisher.stop();
});
it("ConflictNeverStopsExistingStreamAndPermissionFailureNeverReserves", async () => {
  const c = setup(); c.api.reservePublication.mockRejectedValue(new RoomApiError(409)); await c.publisher.start(false);
  expect(c.state().error).toMatch(/already|another/i); expect(c.api.stopPublication).not.toHaveBeenCalled(); expect(c.stream.tracks[0]!.stop).toHaveBeenCalledOnce();
  const d = setup(); d.deps.capture = async () => { throw new DOMException("private chooser detail", "NotAllowedError"); }; await d.publisher.start(true);
  expect(d.state()).toMatchObject({ phase: "idle", streamId: null }); expect(d.state().error).not.toContain("private"); expect(d.api.reservePublication).not.toHaveBeenCalled();
});
it("StaleTransportFailureCannotStopRestartedPublication", async () => {
  const c = setup(); await c.publisher.start(false); const oldFailure = c.failures[0]!; await c.publisher.stop();
  c.deps.capture = async () => new Stream().asMedia(); await c.publisher.start(false); oldFailure(new Error("old failure"));
  expect(c.state().phase).toBe("live"); expect(c.api.stopPublication).toHaveBeenCalledTimes(1); await c.publisher.stop();
});
it("VideoEndingAndCleanupFailuresStillReleaseOwnedResources", async () => {
  const c = setup(); await c.publisher.start(false); c.transport.close.mockRejectedValue(new Error("network")); c.api.stopPublication.mockRejectedValue(new Error("network"));
  c.stream.tracks[0]!.end(); await vi.waitFor(() => expect(c.state().phase).toBe("idle"));
  expect(c.api.stopPublication).toHaveBeenCalledOnce(); expect(c.transport.close).toHaveBeenCalledOnce();
});
it("LeaseFailureStopsBeforeExpiryEvenWhenRenewalHangs", async () => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "performance", "Date"] });
  const c = setup(); await c.publisher.start(false);
  c.api.renewPublication.mockImplementation(async () => new Promise(() => {}));
  await vi.advanceTimersByTimeAsync(15000); expect(c.api.renewPublication).toHaveBeenCalledOnce();
  await vi.advanceTimersByTimeAsync(35000); expect(c.stream.tracks[0]!.stop).toHaveBeenCalledOnce(); expect(c.state().phase).toBe("idle"); expect(vi.getTimerCount()).toBe(0);
});
it("SuccessfulRenewalExtendsOnlyTheCurrentAttemptAndRejectionStopsIt", async () => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "performance", "Date"] });
  const c = setup(); await c.publisher.start(false); await vi.advanceTimersByTimeAsync(15000); expect(c.api.renewPublication).toHaveBeenCalledOnce();
  c.api.renewPublication.mockRejectedValue(new RoomApiError(403)); await vi.advanceTimersByTimeAsync(15000);
  expect(c.state().phase).toBe("idle"); expect(c.stream.tracks[0]!.stop).toHaveBeenCalledOnce(); expect(vi.getTimerCount()).toBe(0);
});
it("SnapshotBeforeReservationAndOutOfOrderSnapshotsDoNotCancelStart", async () => {
  const c = setup(); c.publisher.reconcile(room(1, false)); await c.publisher.start(false);
  c.publisher.reconcile(room(1, false)); expect(c.state().phase).toBe("live");
  c.publisher.reconcile(room(3)); c.publisher.reconcile(room(2, false)); expect(c.state().phase).toBe("live");
  c.publisher.reconcile(room(4, false)); await vi.waitFor(() => expect(c.state().phase).toBe("idle"));
});
it.each(["insecure", "unsupported", "expired", "missing-video"])("UnavailableCaptureDoesNotReserve: %s", async reason => {
  const c = setup(); if (reason === "insecure") c.deps.secureContext = false; if (reason === "unsupported") c.deps.capture = undefined;
  if (reason === "expired") c.deps.sessionExpiresAt = "2000-01-01T00:00:00Z"; if (reason === "missing-video") c.stream.tracks = [];
  await c.publisher.start(false); expect(c.state().phase).toBe("idle"); expect(c.api.reservePublication).not.toHaveBeenCalled();
});
