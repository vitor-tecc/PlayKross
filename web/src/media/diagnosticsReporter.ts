import type { ViewerDiagnostics } from "./playerDiagnostics";

/**
 * Forwards rolling viewer samples to the room host. At most one report is in
 * flight per stream; samples arriving meanwhile are dropped rather than queued,
 * so a slow host never accumulates pending requests.
 */
export function createViewerReporter(streamId: string, signal: AbortSignal, fetcher: typeof fetch = (...args) => fetch(...args)): (sample: ViewerDiagnostics) => void {
  let inFlight = false;
  return sample => {
    if (inFlight || signal.aborted) return;
    inFlight = true;
    const { codec, connectionPath, receivedMbps, ...metrics } = sample;
    void fetcher("/api/diagnostics", {
      method: "POST", credentials: "same-origin", signal: AbortSignal.any([signal, AbortSignal.timeout(3000)]), headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ streamId, role: "viewer", metrics: { ...metrics, receiveMbps: receivedMbps }, metadata: { codec: codec ?? "", connectionPath, provenance: "browser-rolling-5s" } }),
    }).catch(() => { }).finally(() => { inFlight = false; });
  };
}
