import type { ViewerDiagnostics } from "../media/playerDiagnostics";
export function DiagnosticsPanel({ sample }: { sample: ViewerDiagnostics | null }) {
  const value = (v: number | null | undefined, unit: string) => v === null || v === undefined ? "unavailable" : `${v.toFixed(1)} ${unit}`;
  return <details className="diagnostics"><summary>Playback diagnostics</summary>
    <dl><dt>Delivered video</dt><dd>{sample?.width && sample.height ? `${sample.width} × ${sample.height}` : "unavailable"} · {sample?.codec ?? "codec unavailable"}</dd>
      <dt>Received video</dt><dd>{value(sample?.receivedMbps, "Mbps")}</dd>
      <dt>Decoded / presented</dt><dd>{value(sample?.decodedFps, "FPS")} / {value(sample?.presentedFps, "FPS")}</dd>
      <dt>Packet loss / jitter</dt><dd>{value(sample?.packetLossPercent, "%")} / {value(sample?.jitterMs, "ms")}</dd>
      <dt>ICE round trip</dt><dd>{value(sample?.iceRttMs, "ms")} · {sample?.connectionPath ?? "unknown"}</dd>
      <dt>First frame / stalls over 500 ms</dt><dd>{value(sample?.startupMs, "ms")} / {sample?.stalls ?? "unavailable"}</dd>
    </dl><p>Five-second rolling measurements. Presented frames were submitted for composition; physical display timing and end-to-end video latency are unavailable. ICE round trip measures the network. Capture FPS is measured in the broadcaster.</p>
    <button onClick={() => { void fetch("/api/diagnostics", { credentials: "same-origin" }).then(r => { if (!r.ok) throw new Error(); return r.blob(); }).then(blob => { const url = URL.createObjectURL(blob); const a = document.createElement("a"); a.href = url; a.download = "jkiller-diagnostics.json"; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }).catch(() => { alert("Diagnostic export is unavailable. Rejoin the room and retry."); }); }}>Export room diagnostics</button>
  </details>;
}
