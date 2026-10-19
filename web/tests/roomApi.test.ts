import { afterEach, expect, it, vi } from "vitest";
import { roomApi } from "../src/api/roomApi";
afterEach(() => vi.unstubAllGlobals());
it("ReservesOnlyNewPublicationsAndKeepsCleanupAuthenticated", async () => {
  const request = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => new Response("{}", { status: 200 })); vi.stubGlobal("fetch", request);
  const signal = new AbortController().signal;
  await roomApi.reservePublication(signal);
  await roomApi.renewPublication("id/?", signal);
  await roomApi.stopPublication("id/?", signal, true);
  expect(request.mock.calls.map(c => c[0])).toEqual(["/api/streams?requireNew=true", "/api/streams/id%2F%3F/lease", "/api/streams/id%2F%3F"]);
  expect(request).toHaveBeenLastCalledWith("/api/streams/id%2F%3F", expect.objectContaining({ credentials: "same-origin", method: "DELETE", keepalive: true, signal }));
});
