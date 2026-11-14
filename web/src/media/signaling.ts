// Shared WHIP/WHEP signaling helpers. Both sessions use the same proxy
// contract: same-origin `/media/{path}/{whip|whep}` endpoints, bounded
// requests and `/media-resources/{uuid}` resources deleted on close.

export interface OwnedResource { url: URL; token: string }

const resourcePath = /^\/media-resources\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True when the URL is on `origin` and carries no credentials, query or fragment. */
export function isPlainSameOrigin(url: URL, origin: string): boolean {
  return url.origin === origin && !url.search && !url.hash && !url.username && !url.password;
}

/** Validates the media signaling endpoint issued in a playback/publish grant. */
export function isSignalingEndpoint(url: URL, origin: string, path: string, mode: "whip" | "whep"): boolean {
  return isPlainSameOrigin(url, origin) && url.pathname === `/media/${path}/${mode}`;
}

/** Validates the proxied session resource returned in a 201 Location header. */
export function isMediaResource(url: URL, origin: string): boolean {
  return isPlainSameOrigin(url, origin) && resourcePath.test(url.pathname);
}

/** Issues a same-origin signaling request bounded by the session lifetime and a timeout. */
export function signalingRequest(fetcher: typeof fetch, url: URL, init: RequestInit, owner: AbortSignal, timeoutMs: number): Promise<Response> {
  return fetcher(url, { ...init, credentials: "same-origin", signal: AbortSignal.any([owner, AbortSignal.timeout(timeoutMs)]) });
}

/** Best-effort resource deletion; server ICE/lease expiry and revocation remain fallback cleanup. */
export async function deleteResource(fetcher: typeof fetch, resource: OwnedResource | null): Promise<void> {
  if (!resource) return;
  try { await fetcher(resource.url, { method: "DELETE", credentials: "same-origin", headers: { Authorization: `Bearer ${resource.token}` }, keepalive: true, signal: AbortSignal.timeout(5000) }); }
  catch { /* Network loss leaves cleanup to server expiry. */ }
}

export interface PeerWait {
  event: string; ready: () => boolean; failed?: () => boolean; acceptAtDeadline?: () => boolean;
  timeoutMs: number; signal: AbortSignal; failure: () => Error; aborted: () => Error;
}

/** Resolves once `ready()` holds after peer events, rejecting on failure, abort or deadline. */
export function waitForPeer(peer: RTCPeerConnection | null, wait: PeerWait): Promise<void> {
  if (!peer || wait.signal.aborted) return Promise.reject(wait.aborted());
  if (wait.failed?.()) return Promise.reject(wait.failure());
  if (wait.ready()) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const cleanup = () => { clearTimeout(timer); peer.removeEventListener(wait.event, changed); wait.signal.removeEventListener("abort", aborted); };
    const changed = () => {
      if (wait.failed?.()) { cleanup(); reject(wait.failure()); }
      else if (wait.ready()) { cleanup(); resolve(); }
    };
    const aborted = () => { cleanup(); reject(wait.aborted()); };
    const timer = setTimeout(() => { cleanup(); if (wait.acceptAtDeadline?.()) resolve(); else reject(wait.failure()); }, wait.timeoutMs);
    peer.addEventListener(wait.event, changed); wait.signal.addEventListener("abort", aborted, { once: true });
    changed();
  });
}

/** True when the offer already contains at least one ICE candidate. */
export function hasCandidate(peer: RTCPeerConnection | null): boolean {
  return /^a=candidate:/m.test(peer?.localDescription?.sdp ?? "");
}

/**
 * Fails an established connection immediately on `failed`, or after a short
 * grace period on `disconnected` (which can recover by itself). Returns a detach function.
 */
export function superviseConnection(peer: RTCPeerConnection, active: () => boolean, fail: () => void, graceMs = 2000): () => void {
  let timer: ReturnType<typeof setTimeout> | null = null;
  const clear = () => { if (timer) { clearTimeout(timer); timer = null; } };
  const changed = () => {
    clear();
    if (!active()) return;
    if (peer.connectionState === "failed") fail();
    else if (peer.connectionState === "disconnected") timer = setTimeout(fail, graceMs);
  };
  peer.addEventListener("connectionstatechange", changed);
  return () => { clear(); peer.removeEventListener("connectionstatechange", changed); };
}

/** Parses RFC 8288 `Link: <stun:...>; rel="ice-server"` headers returned by WHIP/WHEP OPTIONS. */
export function parseIceServers(link: string | null): RTCIceServer[] {
  if (!link) return [];
  const servers: RTCIceServer[] = [];
  for (const match of link.matchAll(/<((?:stun|stuns|turn|turns):[^>]+)>((?:[^,<]|"[^"]*")*)/g)) {
    const params = match[2] ?? "";
    if (!/rel="ice-server"/.test(params)) continue;
    const username = /username="([^"\\]*)"/.exec(params)?.[1];
    const credential = /credential="([^"\\]*)"/.exec(params)?.[1];
    servers.push({ urls: match[1]!, ...(username !== undefined ? { username } : {}), ...(credential !== undefined ? { credential } : {}) });
  }
  return servers;
}

/** True when the browser can use the codec in the given direction. */
export function supportsCodec(direction: "send" | "receive", kind: "audio" | "video", mimeType: string): boolean {
  const api = direction === "send" ? globalThis.RTCRtpSender : globalThis.RTCRtpReceiver;
  if (typeof api === "undefined") return false;
  const wanted = mimeType.toLowerCase();
  return Boolean(api.getCapabilities(kind)?.codecs.some(codec => codec.mimeType.toLowerCase() === wanted));
}
