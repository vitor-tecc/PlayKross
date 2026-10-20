import { afterEach, expect, it, vi } from "vitest";
import { WhipSession, type WhipDependencies } from "../src/media/WhipSession";
import { audioSdp, deferred, grant, Peer, Stream, Track, videoSdp } from "./publisherFakes";
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
const resource = "https://room.test/media-resources/12345678-1234-1234-1234-123456789abc";
function created(body = videoSdp, location = resource) { return new Response(body, { status: 201, headers: { Location: location, "Content-Type": "application/sdp" } }); }
function setup(reply?: (url: RequestInfo | URL, init?: RequestInit) => Promise<Response>) {
  const peer = new Peer(), stream = new Stream(), failed = vi.fn();
  vi.stubGlobal("RTCRtpSender", { getCapabilities: (kind: string) => ({ codecs: [{ mimeType: kind === "video" ? "video/H264" : "audio/opus", clockRate: 90000 }] }) });
  const fetch = vi.fn(reply ?? (async (_url, init) => init?.method === "POST" ? created() : new Response(null, { status: 204 })));
  const deps: WhipDependencies = { origin: "https://room.test", fetch: fetch as typeof globalThis.fetch, createPeer: () => peer as unknown as RTCPeerConnection, supportsCodecs: () => true, timeoutMs: 25 };
  return { peer, stream, fetch, failed, deps, session: new WhipSession(deps, failed) };
}
it("UsesOneSendOnlyEncodingAndCompatibleCodecs", async () => {
  const c = setup(async (_url, init) => init?.method === "POST" ? created(videoSdp + audioSdp) : new Response(null, { status: 204 }));
  c.stream.tracks.push(new Track("audio"));
  await c.session.start(grant, c.stream.asMedia(), new AbortController().signal);
  expect(c.peer.transceivers.map(t => [t.track.kind, t.direction])).toEqual([["video", "sendonly"], ["audio", "sendonly"]]);
  expect(c.peer.transceivers[0]!.sendEncodings).toEqual([{ maxBitrate: 12_000_000, maxFramerate: 60 }]);
  expect(c.peer.transceivers[0]!.sender.setParameters).toHaveBeenCalledWith(expect.objectContaining({ encodings: [{ maxBitrate: 12_000_000, maxFramerate: 60 }] }));
  expect(c.peer.transceivers[1]!.sendEncodings).toBeUndefined();
  expect(c.peer.transceivers[0]!.setCodecPreferences).toHaveBeenCalledWith([expect.objectContaining({ mimeType: "video/H264" })]);
  await c.session.close(); await c.session.close();
  expect(c.peer.close).toHaveBeenCalledTimes(1);
  expect(c.fetch.mock.calls.filter(([, i]) => i?.method === "DELETE").map(([url]) => String(url))).toEqual([resource]);
  expect(c.stream.tracks[0]!.stop).not.toHaveBeenCalled();
});
it.each(["https://foreign.test/whip", "/media/wrong/whip", grant.whipUri + "?secret=yes"])("RejectsForeignOrMalformedEndpoints: %s", async whipUri => {
  const c = setup(); await expect(c.session.start({ ...grant, whipUri }, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow(); expect(c.fetch).not.toHaveBeenCalled();
});
it.each(["https://foreign.test/resource", "/media-resources/not-a-guid", resource + "?private=yes", ""])("RejectsUnexpectedResource: %s", async location => {
  const c = setup(async (_url, init) => init?.method === "POST" ? created(videoSdp, location) : new Response(null, { status: 204 }));
  await expect(c.session.start(grant, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow();
  expect(c.fetch.mock.calls.some(([url]) => String(url).includes("foreign"))).toBe(false); expect(c.peer.close).toHaveBeenCalledOnce();
});
it("LateCreatedResourceAfterStopIsDeleted", async () => {
  const response = deferred<Response>(), posted = deferred<void>();
  const c = setup(async (_url, init) => { if (init?.method === "POST") { posted.resolve(); return response.promise; } return new Response(null, { status: 204 }); });
  const start = c.session.start(grant, c.stream.asMedia(), new AbortController().signal); const rejected = expect(start).rejects.toThrow();
  await posted.promise; await c.session.close(); response.resolve(created()); await rejected;
  expect(c.fetch.mock.calls.filter(([, i]) => i?.method === "DELETE")).toHaveLength(1); expect(c.peer.setRemoteDescription).not.toHaveBeenCalled();
});
it.each(["not-sdp", videoSdp.replace("H264", "VP8"), videoSdp.replace("m=video 9", "m=video 0")])("InvalidAnswerReleasesKnownResource", async body => {
  const c = setup(async (_url, init) => init?.method === "POST" ? created(body) : new Response(null, { status: 204 }));
  await expect(c.session.start(grant, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow();
  expect(c.fetch.mock.calls.filter(([, i]) => i?.method === "DELETE")).toHaveLength(1);
});
it.each([401, 403, 404, 500])("HttpFailuresReleaseThePeer: %i", async status => {
  const c = setup(async (_url, init) => init?.method === "POST" ? new Response("private server details", { status }) : new Response(null, { status: 204 }));
  await expect(c.session.start(grant, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow(/broadcast/i); expect(c.peer.close).toHaveBeenCalledOnce();
});
it("CancellationAndExpiredGrantDoNotPublish", async () => {
  const c = setup(), abort = new AbortController(); abort.abort();
  await expect(c.session.start(grant, c.stream.asMedia(), abort.signal)).rejects.toThrow(); expect(c.fetch).not.toHaveBeenCalled();
  const expired = setup(); await expect(expired.session.start({ ...grant, expiresAt: "2000-01-01T00:00:00Z" }, expired.stream.asMedia(), new AbortController().signal)).rejects.toThrow(); expect(expired.fetch).not.toHaveBeenCalled();
});
it("MissingVideoAndUnsupportedCodecsDoNotPublish", async () => {
  for (const noVideo of [true, false]) {
    const c = setup(); if (noVideo) c.stream.tracks = []; else c.deps.supportsCodecs = () => false;
    await expect(c.session.start(grant, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow(); expect(c.fetch).not.toHaveBeenCalled();
  }
});
it("IceAndConnectionTimeoutsReleaseResources", async () => {
  const c = setup(); c.peer.iceGatheringState = "gathering"; c.peer.localDescription.sdp = "v=0\r\n";
  await expect(c.session.start(grant, c.stream.asMedia(), new AbortController().signal)).rejects.toThrow(); expect(c.peer.close).toHaveBeenCalledOnce();
  const d = setup(); d.peer.setRemoteDescription = vi.fn(async () => {});
  await expect(d.session.start(grant, d.stream.asMedia(), new AbortController().signal)).rejects.toThrow(); expect(d.fetch.mock.calls.filter(([, i]) => i?.method === "DELETE")).toHaveLength(1);
});
it("VideoOnlyNeedsNoOpusAndLiveFailureNotifiesOnce", async () => {
  const c = setup(); c.deps.supportsCodecs = audio => !audio;
  await c.session.start(grant, c.stream.asMedia(), new AbortController().signal);
  c.peer.connectionState = "failed"; c.peer.dispatchEvent(new Event("connectionstatechange"));
  await vi.waitFor(() => expect(c.failed).toHaveBeenCalledOnce()); expect(c.peer.close).toHaveBeenCalledOnce();
});
