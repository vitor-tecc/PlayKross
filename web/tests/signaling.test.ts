import { afterEach, expect, it, vi } from "vitest";
import { deleteResource, isMediaResource, isSignalingEndpoint, parseIceServers, superviseConnection, waitForPeer } from "../src/media/signaling";
afterEach(() => vi.useRealTimers());
class Peer extends EventTarget {
  connectionState: RTCPeerConnectionState = "connected";
  set(state: RTCPeerConnectionState) { this.connectionState = state; this.dispatchEvent(new Event("connectionstatechange")); }
}
const origin = "https://room.test";
const asPeer = (peer: Peer) => peer as unknown as RTCPeerConnection;

it("ValidatesSignalingEndpointsAndResourcesStrictly", () => {
  const path = "rooms/a/streams/b/main";
  expect(isSignalingEndpoint(new URL(`/media/${path}/whep`, origin), origin, path, "whep")).toBe(true);
  expect(isSignalingEndpoint(new URL(`/media/${path}/whep`, origin), origin, path, "whip")).toBe(false);
  expect(isSignalingEndpoint(new URL(`/media/${path}/whep?x=1`, origin), origin, path, "whep")).toBe(false);
  expect(isSignalingEndpoint(new URL(`https://user:pw@room.test/media/${path}/whep`), origin, path, "whep")).toBe(false);
  expect(isMediaResource(new URL("/media-resources/12345678-1234-1234-1234-123456789abc", origin), origin)).toBe(true);
  expect(isMediaResource(new URL("/media-resources/------------------------------------", origin), origin)).toBe(false);
  expect(isMediaResource(new URL("https://other.test/media-resources/12345678-1234-1234-1234-123456789abc"), origin)).toBe(false);
});

it("ParsesOnlyIceServerLinksWithCredentials", () => {
  const link = `<stun:stun.test:3478>; rel="ice-server", <turn:turn.test>; rel="ice-server"; username="u"; credential="c", <https://x.test>; rel="other"`;
  expect(parseIceServers(link)).toEqual([{ urls: "stun:stun.test:3478" }, { urls: "turn:turn.test", username: "u", credential: "c" }]);
  expect(parseIceServers(null)).toEqual([]);
});

it("WaitForPeerResolvesRejectsAndHonorsDeadlineAcceptance", async () => {
  vi.useFakeTimers();
  const peer = new Peer(), abort = new AbortController();
  const base = { event: "connectionstatechange", timeoutMs: 100, signal: abort.signal, failure: () => new Error("failed"), aborted: () => new Error("aborted") };
  const states = { ready: () => peer.connectionState === "connected", failed: () => peer.connectionState === "failed" };
  peer.connectionState = "connecting";
  const ready = waitForPeer(asPeer(peer), { ...base, ...states });
  peer.set("connected"); await expect(ready).resolves.toBeUndefined();
  peer.connectionState = "connecting";
  const failed = waitForPeer(asPeer(peer), { ...base, ...states });
  peer.set("failed"); await expect(failed).rejects.toThrow("failed");
  const accepted = waitForPeer(asPeer(peer), { ...base, ready: () => false, acceptAtDeadline: () => true });
  vi.advanceTimersByTime(100); await expect(accepted).resolves.toBeUndefined();
  const expired = waitForPeer(asPeer(peer), { ...base, ready: () => false });
  vi.advanceTimersByTime(100); await expect(expired).rejects.toThrow("failed");
  const aborted = waitForPeer(asPeer(peer), { ...base, ready: () => false });
  abort.abort(); await expect(aborted).rejects.toThrow("aborted");
  await expect(waitForPeer(null, { ...base, ready: () => true })).rejects.toThrow("aborted");
});

it("SupervisionFailsImmediatelyOnFailureAndAfterGraceOnDisconnect", () => {
  vi.useFakeTimers();
  const peer = new Peer(), fail = vi.fn(); let active = true;
  const detach = superviseConnection(asPeer(peer), () => active, fail, 2000);
  peer.set("disconnected"); vi.advanceTimersByTime(1999); peer.set("connected"); vi.advanceTimersByTime(5000);
  expect(fail).not.toHaveBeenCalled();
  peer.set("disconnected"); vi.advanceTimersByTime(2000); expect(fail).toHaveBeenCalledOnce();
  peer.set("failed"); expect(fail).toHaveBeenCalledTimes(2);
  active = false; peer.set("failed"); expect(fail).toHaveBeenCalledTimes(2);
  active = true; peer.set("disconnected"); detach(); vi.advanceTimersByTime(5000); peer.set("failed");
  expect(fail).toHaveBeenCalledTimes(2);
});

it("DeleteResourceIsBestEffortAndAuthenticated", async () => {
  const fetcher = vi.fn(async (_url: URL, _init: RequestInit) => { throw new TypeError("offline"); });
  await expect(deleteResource(fetcher as unknown as typeof fetch, { url: new URL("/media-resources/x", origin), token: "t" })).resolves.toBeUndefined();
  expect(fetcher).toHaveBeenCalledWith(expect.any(URL), expect.objectContaining({ method: "DELETE", keepalive: true, headers: { Authorization: "Bearer t" } }));
  await deleteResource(fetcher as unknown as typeof fetch, null);
  expect(fetcher).toHaveBeenCalledOnce();
});
