import type { PublisherSnapshot } from "../media/BrowserPublisher";
import { useState } from "react";
export interface BroadcastControlsProps { state: PublisherSnapshot; unavailableReason: string | null; onStart: (shareAudio: boolean) => void; onStop: () => void }
export function BroadcastControls({ state, unavailableReason, onStart, onStop }: BroadcastControlsProps) {
  const [shareAudio, setShareAudio] = useState(false);
  const active = state.phase !== "idle";
  const status = state.phase === "choosing" ? "Choose a screen, window or tab in your browser" : state.phase === "starting" ? "Connecting your broadcast…" :
    state.phase === "stopping" ? "Stopping sharing…" : state.phase === "live" ? state.hasAudio ? "Sharing video and captured audio" : "Sharing video only" : "Share from this browser";
  return <section className="broadcast-controls" aria-label="Screen sharing controls">
    <div className="broadcast-actions"><strong role="status">{status}</strong>
      <label><input type="checkbox" checked={shareAudio} disabled={active} onChange={event => setShareAudio(event.target.checked)} />Share captured audio</label>
      {active ? <button disabled={state.phase === "stopping"} onClick={onStop}>Stop sharing</button> : <button disabled={Boolean(unavailableReason)} onClick={() => onStart(shareAudio)}>Start sharing</button>}
    </div>
    {state.error && <p role="alert">{state.error}</p>}
    {unavailableReason && <p>{unavailableReason}</p>}
    {state.phase === "live" && shareAudio && !state.hasAudio && <p role="status">Video only: captured audio is unavailable or has ended.</p>}
    <p>Your browser chooses the source and encoder. Audio depends on the browser and selected source; no microphone is requested.</p>
    {shareAudio && <p>Room sound in this tab is muted during captured-audio sharing to prevent feedback. Audio from other tabs or apps may still be captured.</p>}
  </section>;
}
