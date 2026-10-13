import { describe, expect, it, vi } from "vitest";
import { WhepSession, type WhepDependencies } from "../src/media/WhepSession";
import type { PlaybackGrant } from "../src/api/contracts";

const grant: PlaybackGrant = { streamId: "id", path: "rooms/11111111111111111111111111111111/streams/22222222222222222222222222222222/main", whepUri: "/media/rooms/11111111111111111111111111111111/streams/22222222222222222222222222222222/main/whep", token: "media-credential", expiresAt: "2026-10-13T13:00:00Z" };
class FakeStream {
  tracks: MediaStreamTrack[] = [];
  getTracks() { return this.tracks; }
  addTrack(track: MediaStreamTrack) { this.tracks.push(track); }
}
class FakePeer extends EventTarget {
  connectionState: RTCPeerConnectionState = "new";
  iceGatheringState = "complete";
  localDescription = { type: "offer", sdp: "v=0\r\n" };
  track = { id: "audio", stop: vi.fn() } as unknown as MediaStreamTrack;
  addTransceiver = vi.fn(() => ({ setCodecPreferences: vi.fn() }));
  createOffer = vi.fn(async () => this.localDescription);
  setLocalDescription = vi.fn(async () => {});
  setRemoteDescription = vi.fn(async () => {
    this.connectionState = "connected";
    this.dispatchEvent(Object.assign(new Event("track"), { track: this.track }));
    this.dispatchEvent(new Event("connectionstatechange"));
  });
  close = vi.fn(() => { this.connectionState = "closed"; });
  getReceivers() { return [{ track: this.track }]; }
}
function setup(response?: (request: RequestInfo | URL, init?: RequestInit) => Promise<Response>) {
  const peer = new FakePeer(); const stream = new FakeStream();
  const request = vi.fn(response ?? (async (_url, init) => init?.method === "POST" ? new Response("v=0\r\n", { status: 201, headers: { Location: "/media-resources/12345678-1234-1234-1234-123456789abc", "Content-Type": "application/sdp" } }) : new Response(null, { status: 204 })));
  const deps: WhepDependencies = { origin: "https://room.test", createPeer: () => peer as unknown as RTCPeerConnection, createStream: () => stream as unknown as MediaStream, fetch: request as typeof fetch, supportsCodecs: () => true, timeoutMs: 100 };
  return { peer, stream, request, session: new WhepSession(deps) };
}
describe("WHEP ownership", () => {
  it("CloseDuringHandshakeDeletesLateResource", async () => {
    let finish!: (response: Response) => void; let started!: () => void;
    const entered = new Promise<void>(resolve => { started = resolve; });
    const context = setup(async (_url, init) => { if (init?.method === "POST") { started(); return new Promise(resolve => { finish = resolve; }); } return new Response(null, { status: 204 }); });
    const start = context.session.start(grant, new AbortController().signal);
    const rejection = expect(start).rejects.toMatchObject({ name: "AbortError" });
    await entered; await context.session.close();
    finish(new Response("v=0", { status: 201, headers: { Location: "/media-resources/12345678-1234-1234-1234-123456789abc", "Content-Type": "application/sdp" } }));
    await rejection;
    expect(context.peer.close).toHaveBeenCalledOnce();
    expect(context.request.mock.calls.filter(([, init]) => init?.method === "DELETE")).toHaveLength(1);
    expect(context.peer.setRemoteDescription).not.toHaveBeenCalled();
  });
  it("RetryDoesNotDuplicateAudio", async () => {
    const first = setup(); await first.session.start(grant, new AbortController().signal); await first.session.close(); await first.session.close();
    const second = setup(); await second.session.start(grant, new AbortController().signal);
    expect(first.peer.close).toHaveBeenCalledOnce(); expect(first.peer.track.stop).toHaveBeenCalled(); expect(second.stream.tracks).toHaveLength(1);
    expect(first.request.mock.calls.filter(([, init]) => init?.method === "DELETE")).toHaveLength(1);
    await second.session.close();
  });
  it("RejectsUnexpectedResourceOrigin", async () => {
    const context = setup(async (_url, init) => init?.method === "POST" ? new Response("v=0", { status: 201, headers: { Location: "https://untrusted.test/resource", "Content-Type": "application/sdp" } }) : new Response(null, { status: 204 }));
    await expect(context.session.start(grant, new AbortController().signal)).rejects.toThrow(/resource/i);
    expect(context.request.mock.calls.some(([url]) => String(url).includes("untrusted"))).toBe(false);
    expect(context.peer.close).toHaveBeenCalledOnce();
  });
  it("FailedIceGivesActionableState", async () => {
    const context = setup(); context.peer.setRemoteDescription = vi.fn(async () => { context.peer.connectionState = "failed"; context.peer.dispatchEvent(new Event("connectionstatechange")); });
    await expect(context.session.start(grant, new AbortController().signal)).rejects.toThrow(/UDP|firewall|relay/);
    expect(context.peer.close).toHaveBeenCalledOnce();
  });
});
