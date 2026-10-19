import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { RoomSnapshot } from "../src/api/contracts";
import { RoomPage } from "../src/room/RoomPage";
import { grant, Stream, Track, deferred } from "./publisherFakes";
const sender = vi.hoisted(() => ({ start: vi.fn(async () => {}), close: vi.fn(async () => {}) }));
vi.mock("../src/media/WhipSession", () => ({ WhipSession: class { start = sender.start; close = sender.close; } }));
const api = vi.hoisted(() => ({ me: vi.fn(), join: vi.fn(), rename: vi.fn(), leave: vi.fn(), playback: vi.fn(), reservePublication: vi.fn(), renewPublication: vi.fn(), stopPublication: vi.fn(), watchRoom: vi.fn(async (_changed: (room: RoomSnapshot) => void, _signal: AbortSignal) => async () => {}) }));
vi.mock("../src/api/roomApi", () => ({ roomApi: api, RoomApiError: class extends Error { status = 401; } }));
afterEach(() => { cleanup(); delete window.playkrossPlaybackAudioBlocked; sessionStorage.clear(); history.replaceState(null, "", "/"); vi.clearAllMocks(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
function sharingSetup() {
  const stream = new Stream([new Track("video"), new Track("audio")]);
  const capture = vi.fn(async () => stream.asMedia());
  vi.stubGlobal("isSecureContext", true); vi.stubGlobal("navigator", { mediaDevices: { getDisplayMedia: capture } });
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "2099-01-01T00:00:00Z", room: { roomId: "room", revision: 1, participants: [{ sessionId: "me", nickname: "Me" }], streams: [{ streamId: "other", ownerSessionId: "friend", nickname: "Friend", status: "live" }] } });
  api.reservePublication.mockResolvedValue(grant); api.stopPublication.mockResolvedValue({ roomId: "room", revision: 3, participants: [], streams: [] });
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(); vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
  return { stream, capture };
}
it("NoCaptureBeforeStartGestureAndStopWorksDuringChooser", async () => {
  const c = sharingSetup(), chosen = deferred<MediaStream>(); c.capture.mockImplementation(() => chosen.promise);
  render(<RoomPage />); const start = await screen.findByRole("button", { name: "Start sharing" }); expect(c.capture).not.toHaveBeenCalled();
  fireEvent.click(start); expect(c.capture).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Stop sharing" }));
  await act(async () => chosen.resolve(c.stream.asMedia()));
  await waitFor(() => expect(c.stream.tracks[0]!.stop).toHaveBeenCalled()); expect(api.reservePublication).not.toHaveBeenCalled();
});
it("AudioMuteIsSynchronousAndRestoresPreferencesWithoutClearingNativeBlock", async () => {
  const c = sharingSetup(), view = render(<RoomPage />); await screen.findByRole("button", { name: "Start sharing" });
  const video = view.container.querySelector("video")!;
  api.playback.mockResolvedValue({ streamId: "other" });
  fireEvent.click(screen.getByRole("button", { name: "Watch stream" }));
  fireEvent.click(await screen.findByRole("button", { name: "Enable sound" }));
  expect(video.muted).toBe(false);
  fireEvent.change(screen.getByRole("slider", { name: "Friend's volume" }), { target: { value: "35" } });
  fireEvent.click(screen.getByLabelText("Share captured audio"));
  let mutedAtCapture = false;
  c.capture.mockImplementation(async () => { mutedAtCapture = video.muted; return c.stream.asMedia(); });
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" })); expect(mutedAtCapture).toBe(true); await screen.findByText("Sharing video and captured audio");
  await act(async () => { window.playkrossPlaybackAudioBlocked = true; window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: true })); });
  fireEvent.click(screen.getByRole("button", { name: "Stop sharing" })); await waitFor(() => expect(c.stream.tracks[0]!.stop).toHaveBeenCalled());
  expect(video.muted).toBe(true); expect(video.volume).toBe(.35);
  await act(async () => { window.playkrossPlaybackAudioBlocked = false; window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: false })); });
  expect(video.muted).toBe(false); expect(video.volume).toBe(.35);
});
it("OwnPublicationDoesNotCreateAViewerAndViewingTabDepartureDoesNotLeave", async () => {
  sharingSetup(); let changed!: (room: RoomSnapshot) => void; api.watchRoom.mockImplementationOnce(async update => { changed = update; return async () => {}; });
  const view = render(<RoomPage />); fireEvent.click(await screen.findByRole("button", { name: "Start sharing" })); await screen.findByText("Sharing video only");
  await act(async () => changed({ roomId: "room", revision: 2, participants: [], streams: [{ streamId: grant.streamId, ownerSessionId: "me", nickname: "Me", status: "live" }] }));
  const own = view.container.querySelector(`[data-stream-id="${grant.streamId}"]`)!;
  expect(own).not.toBeNull(); expect(own.querySelector("video")).toBeNull(); expect(api.playback).not.toHaveBeenCalled();
  await act(async () => window.dispatchEvent(new Event("pagehide"))); expect(api.stopPublication).toHaveBeenCalledOnce(); expect(api.leave).not.toHaveBeenCalled();
});
it("LeavingStopsOwnedCaptureBeforeMembershipRelease", async () => {
  const c = sharingSetup(); api.leave.mockImplementation(async () => { expect(c.stream.tracks[0]!.stop).toHaveBeenCalledOnce(); expect(sender.close).toHaveBeenCalled(); return {}; });
  render(<RoomPage />); fireEvent.click(await screen.findByRole("button", { name: "Start sharing" })); await screen.findByText("Sharing video only");
  fireEvent.click(screen.getByRole("button", { name: "Leave room" })); await waitFor(() => expect(api.leave).toHaveBeenCalled());
});
it("ViewingTabDepartureDoesNotStopAnotherTabsPublication", async () => {
  sharingSetup();
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "2099-01-01T00:00:00Z", room: { roomId: "room", revision: 2, participants: [], streams: [{ streamId: grant.streamId, ownerSessionId: "me", nickname: "Me", status: "live" }] } });
  render(<RoomPage />); await screen.findByRole("button", { name: "Start sharing" });
  expect(screen.queryByRole("button", { name: "Stop sharing" })).toBeNull();
  await act(async () => window.dispatchEvent(new Event("pagehide")));
  expect(api.stopPublication).not.toHaveBeenCalled(); expect(api.leave).not.toHaveBeenCalled();
});
it("AnExplicitInvitationSelectsItsRoomInsteadOfRestoringAnotherRoom", async () => {
  history.replaceState(null, "", "/#invite=new-invitation");
  api.me.mockResolvedValue({ sessionId: "old-session", expiresAt: "later", room: { roomId: "old-room", revision: 1, participants: [{ sessionId: "old-session", nickname: "friend" }], streams: [] } });
  api.join.mockResolvedValue({ sessionId: "new-session", expiresAt: "later", room: { roomId: "new-room", revision: 1, participants: [{ sessionId: "new-session", nickname: "friend" }, { sessionId: "target", nickname: "Target room broadcaster" }], streams: [] } });
  render(<RoomPage />);
  await waitFor(() => expect(api.join).toHaveBeenCalledWith("new-invitation", "friend", expect.any(AbortSignal)));
  expect(await screen.findByText("Target room broadcaster")).toBeDefined();
});
it("LeavingClosesTheSubscriptionBeforeReleasingMembershipAndShowsTheJoinForm", async () => {
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: { roomId: "room", revision: 1, participants: [{ sessionId: "me", nickname: "friend" }], streams: [] } });
  let finishClose!: () => void;
  const closed = new Promise<void>(resolve => { finishClose = resolve; });
  api.watchRoom.mockResolvedValueOnce(async () => closed);
  api.leave.mockResolvedValue({ roomId: "room", revision: 2, participants: [], streams: [] });
  render(<RoomPage />);
  fireEvent.click(await screen.findByRole("button", { name: "Leave room" }));
  expect(api.leave).not.toHaveBeenCalled();
  finishClose();
  await waitFor(() => expect(api.leave).toHaveBeenCalledWith(expect.any(AbortSignal)));
  expect(await screen.findByRole("heading", { name: "Join your friends" })).toBeDefined();
  expect(screen.queryByText("friend · you")).toBeNull();
});

it("TwelveParticipantsSeeTheActualRoomCount", async () => {
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: { roomId: "room", revision: 1, participants: Array.from({ length: 12 }, (_, index) => ({ sessionId: index === 0 ? "me" : `member-${index}`, nickname: `Member ${index}` })), streams: [] } });
  render(<RoomPage />);
  expect(await screen.findByText("12 in the room")).toBeDefined();
  expect(screen.queryByText(/\/ 8/)).toBeNull();
});
it("AConflictOffersRetryWithoutInventingAParticipantOrBroadcasterQuota", async () => {
  const { RoomApiError } = await vi.importActual<typeof import("../src/api/roomApi")>("../src/api/roomApi");
  const error = new RoomApiError(409);
  expect(error.message).toMatch(/retry/i);
  expect(error.message).not.toMatch(/capacity|full|one broadcaster/i);
});
it("RemovingOneBroadcastAndRenamingTheOtherClosesOnlyTheRemovedPeer", async () => {
  const peers: Peer[] = [];
  class Peer extends EventTarget {
    connectionState = "new"; iceGatheringState = "complete";
    localDescription = { type: "offer", sdp: "v=0\r\n" };
    track = { id: `track-${peers.length}`, stop: vi.fn() };
    constructor() { super(); peers.push(this); }
    addTransceiver() { return { setCodecPreferences() {} }; }
    async createOffer() { return this.localDescription; }
    async setLocalDescription() {}
    async setRemoteDescription() { this.connectionState = "connected"; this.dispatchEvent(Object.assign(new Event("track"), { track: this.track })); this.dispatchEvent(new Event("connectionstatechange")); }
    getReceivers() { return [{ track: this.track }]; }
    async getStats() { return new Map(); }
    close() { this.connectionState = "closed"; }
  }
  class Stream {
    tracks: { id: string; stop: () => void }[] = [];
    getTracks() { return this.tracks; }
    addTrack(track: { id: string; stop: () => void }) { this.tracks.push(track); }
  }
  vi.stubGlobal("RTCPeerConnection", Peer); vi.stubGlobal("MediaStream", Stream);
  vi.stubGlobal("RTCRtpReceiver", { getCapabilities: (kind: string) => ({ codecs: [{ mimeType: kind === "video" ? "video/H264" : "audio/opus" }] }) });
  vi.stubGlobal("fetch", vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => init?.method === "POST" ? new Response("v=0\r\n", { status: 201, headers: { Location: `/media-resources/${crypto.randomUUID()}`, "Content-Type": "application/sdp" } }) : new Response(null, { status: 204 })));
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(); vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
  const room: RoomSnapshot = { roomId: "room", revision: 1, participants: [{ sessionId: "a", nickname: "Alice" }, { sessionId: "b", nickname: "Bob" }], streams: [{ streamId: "a-stream", ownerSessionId: "a", nickname: "Alice", status: "reserved" }, { streamId: "b-stream", ownerSessionId: "b", nickname: "Bob", status: "reserved" }] };
  api.me.mockResolvedValue({ sessionId: "a", expiresAt: "later", room });
  api.playback.mockImplementation(async (streamId: string) => ({ streamId, path: `rooms/room/streams/${streamId}/main`, whepUri: `/media/rooms/room/streams/${streamId}/main/whep`, token: `grant-${streamId}`, expiresAt: "later" }));
  const view = render(<RoomPage />);
  await screen.findByText("Alice");
  const cards = view.container.querySelectorAll<HTMLElement>(".stream-card");
  fireEvent.click(within(cards[0]!).getByRole("button", { name: "Watch stream" }));
  fireEvent.click(within(cards[1]!).getByRole("button", { name: "Watch stream" }));
  await waitFor(() => expect(peers.map(peer => peer.connectionState)).toEqual(["connected", "connected"]));
  const bVideo = cards[1]!.querySelector("video")!; const bMedia = bVideo.srcObject;
  fireEvent.change(within(cards[1]!).getByRole("slider", { name: "Bob's volume" }), { target: { value: "35" } });
  fireEvent.click(within(cards[1]!).getByRole("button", { name: "Focus stream" }));
  expect(cards[1]!.getAttribute("data-focused")).toBe("true");
  expect(screen.getByRole("button", { name: "Grid view" })).toBeDefined();
  fireEvent.click(within(cards[0]!).getByRole("button", { name: "Focus stream" }));
  expect(cards[0]!.getAttribute("data-focused")).toBe("true");
  expect(cards[1]!.getAttribute("data-focused")).toBe("false");
  expect(bVideo.volume).toBe(.35);
  expect(view.container.querySelector('[data-stream-id="b-stream"] video')).toBe(bVideo);
  expect(bVideo.srcObject).toBe(bMedia);
  fireEvent.click(screen.getByRole("button", { name: "Grid view" }));
  expect(view.container.querySelectorAll('[data-focused="true"]')).toHaveLength(0);
  fireEvent.click(within(cards[1]!).getByRole("button", { name: "Enable sound" }));
  expect(bVideo.muted).toBe(false);
  act(() => { window.playkrossPlaybackAudioBlocked = true; window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: true })); });
  expect(bVideo.muted).toBe(true); expect(cards[0]!.querySelector("video")!.muted).toBe(true);
  for (const card of cards) expect((within(card).getByRole("button", { name: "Enable sound" }) as HTMLButtonElement).disabled).toBe(true);
  act(() => { window.playkrossPlaybackAudioBlocked = false; window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: false })); });
  expect(bVideo.muted).toBe(false); expect(cards[0]!.querySelector("video")!.muted).toBe(true);
  expect(peers).toHaveLength(2); expect(bVideo.srcObject).toBe(bMedia);
  const changed = api.watchRoom.mock.calls[0]![0];
  act(() => changed({ ...room, revision: 2, streams: [room.streams[1]!] }));
  await waitFor(() => expect(peers[0]!.connectionState).toBe("closed"));
  act(() => changed({ ...room, revision: 3, streams: [{ ...room.streams[1]!, nickname: "Renamed Bob" }] }));
  expect(screen.getByText("Renamed Bob")).toBeDefined();
  expect(peers).toHaveLength(2); expect(peers[1]!.connectionState).toBe("connected");
  expect(bVideo.srcObject).toBe(bMedia); expect(bMedia).not.toBeNull();
  expect(view.container.querySelectorAll(".stream-card")).toHaveLength(1);
});

it("RemovingTheFocusedStreamRestoresGridView", async () => {
  const room: RoomSnapshot = { roomId: "room", revision: 1, participants: [], streams: [
    { streamId: "a", ownerSessionId: "alice", nickname: "Alice", status: "reserved" },
    { streamId: "b", ownerSessionId: "bob", nickname: "Bob", status: "reserved" },
  ] };
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room });
  const view = render(<RoomPage />);
  await screen.findByText("Alice");
  const cards = view.container.querySelectorAll<HTMLElement>(".stream-card");
  fireEvent.click(within(cards[0]!).getByRole("button", { name: "Focus stream" }));
  const changed = api.watchRoom.mock.calls[0]![0];
  act(() => changed({ ...room, revision: 2, streams: [room.streams[1]!] }));
  expect(screen.queryByRole("button", { name: "Grid view" })).toBeNull();
  expect(cards[1]!.isConnected).toBe(true);
  expect(cards[1]!.getAttribute("data-focused")).toBe("false");
});
