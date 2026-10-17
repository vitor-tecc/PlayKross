import type { WhepDiagnostic } from "../media/WhepSession";
import { useState } from "react";

export function ConnectionDiagnostics({ samples }: { samples: readonly WhepDiagnostic[] }) {
  const latest = samples.at(-1);
  const [showReport, setShowReport] = useState(false);
  const embeddedViewer = "playkrossPlaybackAudioBlocked" in window;
  const report = JSON.stringify({ version: 1, browser: navigator.userAgent, samples }, null, 2);
  const download = () => {
    const blob = new Blob([report], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a"); link.href = url; link.download = "playkross-connection-diagnostics.json"; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  return <details className="connection-diagnostics diagnostics">
    <summary aria-label="Connection diagnostics" title="Connection diagnostics">↗</summary>
    <div className="diagnostics-content">
      <strong>Connection diagnostics</strong>
      <p>Last four connection results for this stream, including failed attempts. Export these after a reconnect problem.</p>
      {latest ? <dl>
        <dt>Latest result / stage</dt><dd>{latest.outcome} / {latest.stage} · {latest.failure ?? "no failure"}</dd>
        <dt>OPTIONS / POST</dt><dd>{latest.optionsStatus ?? "not reached"} / {latest.postStatus ?? "not reached"}</dd>
        <dt>Gathering / ICE / connection</dt><dd>{latest.gatheringState ?? "unavailable"} / {latest.iceState ?? "unavailable"} / {latest.connectionState ?? "unavailable"}</dd>
        <dt>Local candidate types</dt><dd>host {latest.candidates.host}, srflx {latest.candidates.srflx}, prflx {latest.candidates.prflx}, relay {latest.candidates.relay}</dd>
        <dt>Total / gathering / POST</dt><dd>{latest.elapsedMs} / {latest.timingsMs.gathering ?? "unavailable"} / {latest.timingsMs.post ?? "unavailable"} ms</dd>
        <dt>Grant time left at POST</dt><dd>{latest.grantRemainingMs ?? "unavailable"} ms (estimated using this device’s clock)</dd>
      </dl> : <p>No connection result yet. Start watching to collect one.</p>}
      <ol>{samples.map((sample, index) => <li key={`${sample.at}-${index}`}>{sample.at}: {sample.outcome} · {sample.stage} · {sample.failure ?? "connected"} · {sample.elapsedMs} ms</li>)}</ol>
      <p>A POST 404 means no playable stream at the media endpoint; a reservation can appear before the publisher is ready. Permission rejection is 401/403. Gathering and ICE failures are recorded separately.</p>
      <p>The export includes browser version, timing, states and candidate counts. It omits room links, tokens, SDP, and network addresses.</p>
      <p>Use Show report to select and copy the report in the in-app viewer. Browser viewers can also download it.</p>
      <button disabled={!samples.length} onClick={() => setShowReport(value => !value)}>{showReport ? "Hide report" : "Show report"}</button>{" "}
      {!embeddedViewer && <button disabled={!samples.length} onClick={download}>Download connection report</button>}
      {showReport && <textarea aria-label="Connection report" readOnly value={report} rows={8} onFocus={event => event.currentTarget.select()} style={{ width: "100%", marginTop: 12 }} />}
    </div>
  </details>;
}
