import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { RoomSnapshot } from "../src/api/contracts";
import { RoomPage } from "../src/room/RoomPage";
const api = vi.hoisted(() => ({ me: vi.fn(), join: vi.fn(), rename: vi.fn(), leave: vi.fn(), playback: vi.fn(), watchRoom: vi.fn(async (_changed: (room: RoomSnapshot) => void, _signal: AbortSignal) => async () => {}) }));
vi.mock("../src/api/roomApi", () => ({ roomApi: api, RoomApiError: class extends Error { status = 401; } }));
afterEach(() => { cleanup(); delete window.playkrossPlaybackAudioBlocked; sessionStorage.clear(); history.replaceState(null, "", "/"); vi.clearAllMocks(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
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
