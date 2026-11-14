import { t, useLanguage } from "../i18n";
import type { PlaybackGrant } from "../api/contracts";
import { useEffect, useRef, useState } from "react";
import { WhepSession, type WhepDiagnostic } from "../media/WhepSession";
import { observePlayer, type ViewerDiagnostics } from "../media/playerDiagnostics";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { useFullscreenChrome } from "./useFullscreenChrome";
import { ConnectionDiagnostics } from "./ConnectionDiagnostics";
import { Icon } from "./Icon";
import { createViewerReporter } from "../media/diagnosticsReporter";
export interface StreamPlayerProps { streamId: string; nickname: string; getPlayback: (id: string, signal: AbortSignal) => Promise<PlaybackGrant>; audioBlocked?: boolean; audioBlockedReason?: string; focused?: boolean; onFocusStream?: () => void }
export function StreamPlayer({ streamId, nickname, getPlayback, audioBlocked = false, audioBlockedReason, focused = false, onFocusStream }: StreamPlayerProps) {
  useLanguage();
  const panel = useRef<HTMLDivElement>(null); const video = useRef<HTMLVideoElement>(null);
  const [watch, setWatch] = useState(false); const [retry, setRetry] = useState(0); const [sound, setSound] = useState(false);
  const [status, setStatus] = useState("Ready to watch"); const [error, setError] = useState(false); const [fullscreen, setFullscreen] = useState(false);
  const [diagnostics, setDiagnostics] = useState<ViewerDiagnostics | null>(null);
  const [connectionDiagnostics, setConnectionDiagnostics] = useState<WhepDiagnostic[]>([]);
  useEffect(() => setConnectionDiagnostics([]), [streamId]);
  const [volume, setVolume] = useState(100);
  useEffect(() => { if (video.current) video.current.volume = volume / 100; }, [volume]);
  const chrome = useFullscreenChrome(fullscreen, error || status.includes("Press Play") || status.startsWith("Fullscreen is unavailable"));
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === panel.current);
    document.addEventListener("fullscreenchange", changed); return () => document.removeEventListener("fullscreenchange", changed);
  }, []);
  useEffect(() => {
    if (!watch) { setStatus("Ready to watch"); setError(false); return; }
    const abort = new AbortController(); const element = video.current;
    let current: WhepSession | null = null; let timer: ReturnType<typeof setTimeout> | null = null;
    let stopDiagnostics: (() => void) | null = null;
    const report = createViewerReporter(streamId, abort.signal);
    const detach = () => { stopDiagnostics?.(); stopDiagnostics = null; setDiagnostics(null); if (element) { element.pause(); element.srcObject = null; } };
    const schedule = (reason: Error, attempt: number) => {
      if (abort.signal.aborted) return;
      detach();
      if (attempt > 3) { setError(true); setStatus(reason.message + " Automatic retries stopped; press Retry when ready."); return; }
      setStatus(`Reconnecting (${attempt}/3): ${reason.message}`);
      timer = setTimeout(() => { void open(attempt); }, [1000, 2000, 4000][attempt - 1]);
    };
    const open = async (attempt: number) => {
      if (abort.signal.aborted) return;
      setError(false); setStatus("Connecting");
      const session = new WhepSession(undefined, reason => { if (current === session) schedule(reason, attempt + 1); }, sample => { if (!abort.signal.aborted) setConnectionDiagnostics(previous => [...previous.slice(-3), sample]); }); current = session;
      try {
        const grant = await getPlayback(streamId, abort.signal);
        if (abort.signal.aborted) { await session.close(); return; }
        const stream = await session.start(grant, abort.signal);
        if (abort.signal.aborted || !element || current !== session) { await session.close(); return; }
        element.srcObject = stream;
        const peer = session.getPeer();
        if (peer) stopDiagnostics = observePlayer(peer, element, sample => { setDiagnostics(sample); report(sample); });
        setStatus("Connected; waiting for frames");
        try { await element.play(); } catch { setStatus("Connected. Press Play to begin playback."); }
      } catch (reason) {
        await session.close();
        if (!abort.signal.aborted) schedule(reason instanceof Error ? reason : new Error("Playback connection failed."), attempt + 1);
      }
    };
    void open(0);
    return () => { abort.abort(); if (timer) clearTimeout(timer); detach(); if (current) void current.close(); };
  }, [streamId, getPlayback, watch, retry]);
  const play = async () => {
    if (!video.current) return;
    try { await video.current.play(); } catch { setStatus("Playback was blocked. Press Play again to allow it."); }
  };
  const toggleSound = async () => {
    if (!audioBlocked) {
      const enabled = !sound;
      setSound(enabled);
      // Apply synchronously so the browser treats play() as part of this gesture.
      if (video.current) video.current.muted = !enabled;
    }
    await play();
  };
  const toggleFullscreen = async () => {
    try { if (document.fullscreenElement === panel.current) await document.exitFullscreen(); else await panel.current?.requestFullscreen(); }
    catch { setStatus("Fullscreen is unavailable in this browser window."); }
  };
  return <div className={`stream-card${chrome.visible ? "" : " fullscreen-chrome-hidden"}`} ref={panel} data-stream-id={streamId} data-focused={focused} data-watching={watch} {...chrome.surfaceProps}>
    <div className="stream-heading stream-overlay stream-overlay-top" {...chrome.overlayProps}><strong>{nickname}</strong><span className={`stream-badge${status === "Playing" ? " is-live" : ""}`}>{t(status === "Playing" ? "Live" : "Stream")}</span>{onFocusStream && <button className="icon-button focus-control" aria-label={t(focused ? "Return to grid" : "Focus stream")} title={t(focused ? "Return to grid" : "Focus stream")} aria-pressed={focused} onClick={onFocusStream}><Icon name={focused ? "grid" : "focus"} /></button>}</div>
    <div className="video-surface">
      <video ref={video} autoPlay playsInline muted={audioBlocked || !sound} aria-label={t("{0}'s stream", nickname)} onPlaying={() => setStatus("Playing")} onWaiting={() => watch && setStatus("Waiting for frames")} />
      {!watch && <button type="button" className="stream-placeholder" aria-label={t("Watch stream")} title={t("Watch stream")} onClick={() => setWatch(true)}><Icon name="eye" /></button>}
      {watch && onFocusStream && !fullscreen && <button className="video-focus-target" aria-label={focused ? t("Return {0}'s stream to grid", nickname) : t("Focus {0}'s stream", nickname)} title={t(focused ? "Return to grid" : "Focus stream")} onClick={onFocusStream} />}
    </div>
    <div className="stream-overlay stream-overlay-bottom" {...chrome.overlayProps}>
    <div className={`stream-status${error || status.includes("Press Play") ? " needs-attention" : ""}`} role="status">{t(status)}</div>
    {audioBlocked && <div className="stream-status needs-attention" role="status">{t(audioBlockedReason ?? "Room sound is muted to prevent recapture. Stop system audio sharing to restore sound, or use application audio.")}</div>}
    <div className="stream-controls">
      {watch && <button className="icon-button watch-control" aria-label={t("Stop watching")} title={t("Stop watching")} onClick={() => setWatch(false)}><Icon name="stop" /></button>}
      {watch && <><button className="icon-button" aria-label={t("Play")} title={t("Play")} onClick={() => { void play(); }}><Icon name="play" /></button><button className="icon-button" disabled={audioBlocked} aria-label={t(sound && !audioBlocked ? "Mute" : "Enable sound")} title={t(audioBlocked ? (audioBlockedReason ?? "Stop system audio sharing to restore room sound.") : sound ? "Mute" : "Enable sound")} onClick={() => { void toggleSound(); }}><Icon name={sound && !audioBlocked ? "sound" : "mute"} /></button></>}
      <label className="volume-control" title={t("Volume: {0}%", volume)}><span className="sr-only">{t("{0}'s volume", nickname)}</span><input type="range" min="0" max="100" step="1" value={volume} onChange={event => setVolume(Number(event.target.value))} aria-valuetext={`${volume}%`} /><output aria-hidden="true">{volume}%</output></label>
      {error && <button className="icon-button" aria-label={t("Retry")} title={t("Retry")} onClick={() => setRetry(value => value + 1)}><Icon name="retry" /></button>}
      <DiagnosticsPanel sample={diagnostics} />
      <ConnectionDiagnostics samples={connectionDiagnostics} />
      <button className="icon-button" aria-label={t(fullscreen ? "Return to room" : "Fullscreen")} title={t(fullscreen ? "Return to room" : "Fullscreen")} onClick={() => { void toggleFullscreen(); }}><Icon name="fullscreen" /></button>
    </div>
    </div>
  </div>;
}
