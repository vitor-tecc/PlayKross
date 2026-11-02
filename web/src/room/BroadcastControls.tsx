import { t, useLanguage } from "../i18n";
import type { PublisherSnapshot } from "../media/BrowserPublisher";
import { useState } from "react";
import { defaultQuality, qualityPreset, validQuality, type BroadcastQuality } from "../media/BroadcastQuality";
export interface BroadcastControlsProps { state: PublisherSnapshot; unavailableReason: string | null; onStart: (shareAudio: boolean, quality: BroadcastQuality) => void; onStop: () => void }
export function BroadcastControls({ state, unavailableReason, onStart, onStop }: BroadcastControlsProps) {
  useLanguage();
  const [shareAudio, setShareAudio] = useState(false);
  const [quality, setQuality] = useState<BroadcastQuality>({ ...defaultQuality });
  const [bitrate, setBitrate] = useState(String(defaultQuality.bitrateMbps));
  const selectedQuality = { ...quality, bitrateMbps: Number(bitrate) };
  const choosePreset = (height: number, fps: number) => { const preset = qualityPreset(height, fps); setQuality(preset); setBitrate(String(preset.bitrateMbps)); };
  const active = state.phase !== "idle";
  const stats = state.stats;
  const limitation = stats?.limitation === "none" ? "No limitation reported" : stats?.limitation === "bandwidth" ? "Network bandwidth" : stats?.limitation === "cpu" ? "CPU load" : stats?.limitation === "other" ? "Other" : "Unavailable";
  const status = state.phase === "choosing" ? "Choose a screen, window or tab in your browser" : state.phase === "starting" ? "Connecting your broadcast…" :
    state.phase === "stopping" ? "Stopping sharing…" : state.phase === "live" ? state.hasAudio ? "Sharing video and captured audio" : "Sharing video only" : "Share from this browser";
  return <section className="broadcast-controls" aria-label={t("Screen sharing controls")}>
    <div className="broadcast-actions"><strong role="status">{t(status)}</strong>
      <label><input type="checkbox" checked={shareAudio} disabled={active} onChange={event => setShareAudio(event.target.checked)} />{t("Share captured audio")}</label>
      {active ? <button disabled={state.phase === "stopping"} onClick={onStop}>{t("Stop sharing")}</button> : <button disabled={Boolean(unavailableReason) || !validQuality(selectedQuality)} onClick={() => onStart(shareAudio, selectedQuality)}>{t("Start sharing")}</button>}
    </div>
    <div className="broadcast-quality">
      <label>{t("Resolution")}<select disabled={active} value={quality.height} onChange={event => choosePreset(Number(event.target.value), quality.fps)}>
        <option value="480">480p</option><option value="720">720p</option><option value="1080">1080p</option>
      </select></label>
      <label>{t("Frame rate")}<select disabled={active} value={quality.fps} onChange={event => choosePreset(quality.height, Number(event.target.value))}>
        <option value="30">30 FPS</option><option value="60">60 FPS</option>
      </select></label>
      <label>{t("Video bitrate (Mbps)")}<input type="number" min="1" max="25" step="0.5" disabled={active} value={bitrate} onChange={event => setBitrate(event.target.value)} /></label>
    </div>
    <p>{t("Resolution and FPS select a preset bitrate. Adjust 1–25 Mbps in 0.5 Mbps steps. Stop sharing to change quality.")}</p>
    <p>{t("These are requested limits. Actual quality depends on the source, browser, computer and network; higher settings may affect your game.")}</p>
    {state.phase === "live" && <details className="broadcast-upload"><summary>{t("Upload diagnostics")}</summary>
      <dl>
        <div><dt>{t("Actual video bitrate")}</dt><dd>{stats?.bitrateMbps == null ? t("Measuring…") : `${stats.bitrateMbps.toFixed(2)} Mbps`}</dd></div>
        <div><dt>{t("Sent resolution")}</dt><dd>{stats?.width != null && stats.height != null ? `${stats.width} × ${stats.height}` : t("Unavailable")}</dd></div>
        <div><dt>{t("Sent frame rate")}</dt><dd>{stats?.fps == null ? t("Unavailable") : `${Math.round(stats.fps)} FPS`}</dd></div>
        <div><dt>{t("Browser quality limitation")}</dt><dd>{t(limitation)}</dd></div>
      </dl><p>{t("Video upload samples update every two seconds and exclude audio and network overhead. Browser-reported limitations do not identify every cause of poor quality.")}</p>
    </details>}
    {state.error && <p role="alert">{t(state.error)}</p>}
    {unavailableReason && <p>{t(unavailableReason)}</p>}
    {state.phase === "live" && shareAudio && !state.hasAudio && <p role="status">{t("Video only: captured audio is unavailable or has ended.")}</p>}
    <p>{t("Your browser chooses the source and encoder. Audio depends on the browser and selected source; no microphone is requested.")}</p>
    {shareAudio && <p>{t("Room sound in this tab is muted during captured-audio sharing to prevent feedback. Audio from other tabs or apps may still be captured.")}</p>}
  </section>;
}
