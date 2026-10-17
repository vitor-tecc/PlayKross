import type { PlaybackGrant } from "../api/contracts";
import { useEffect, useRef, useState } from "react";
import { WhepSession, type WhepDiagnostic } from "../media/WhepSession";
import { observePlayer, type ViewerDiagnostics } from "../media/playerDiagnostics";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { useFullscreenChrome } from "./useFullscreenChrome";
import { ConnectionDiagnostics } from "./ConnectionDiagnostics";
export interface StreamPlayerProps { streamId: string; nickname: string; getPlayback: (id: string, signal: AbortSignal) => Promise<PlaybackGrant>; audioBlocked?: boolean; focused?: boolean; onFocusStream?: () => void }
type IconName = "eye" | "stop" | "play" | "sound" | "mute" | "fullscreen" | "focus" | "grid" | "retry";
function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, React.ReactNode> = {
    eye: <><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z" /><circle cx="12" cy="12" r="3" /></>,
    stop: <rect x="6" y="6" width="12" height="12" rx="1" />,
    play: <path d="m8 5 11 7-11 7V5Z" />,
    sound: <><path d="M11 4 5 9H2v6h3l6 5V4Z" /><path d="M15 8a6 6 0 0 1 0 8m3-11a10 10 0 0 1 0 14" /></>,
    mute: <><path d="M11 4 5 9H2v6h3l6 5V4Z" /><path d="m16 9 6 6m0-6-6 6" /></>,
    fullscreen: <path d="M9 3H3v6m12-6h6v6M3 15v6h6m6 0h6v-6" />,
    focus: <><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M16 4v16M16 12h5" /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><rect x="14" y="14" width="7" height="7" rx="1" /></>,
    retry: <><path d="M20 7v5h-5M4 17v-5h5" /><path d="M6 7a7 7 0 0 1 12-1l2 6M4 12l2 6a7 7 0 0 0 12-1" /></>,
  };
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}
export function StreamPlayer({ streamId, nickname, getPlayback, audioBlocked = false, focused = false, onFocusStream }: StreamPlayerProps) {
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
    let reporting = false;
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
        if (peer) stopDiagnostics = observePlayer(peer, element, sample => {
          setDiagnostics(sample);
          const { codec, connectionPath, ...metrics } = sample;
          const { receivedMbps, ...remaining } = metrics;
          if (!reporting) {
            reporting = true;
            void fetch("/api/diagnostics", { method: "POST", credentials: "same-origin", signal: AbortSignal.any([abort.signal, AbortSignal.timeout(3000)]), headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ streamId, role: "viewer", metrics: { ...remaining, receiveMbps: receivedMbps }, metadata: { codec: codec ?? "", connectionPath, provenance: "browser-rolling-5s" } }) }).catch(() => { }).finally(() => { reporting = false; });
          }
        });
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
  const play = async (enableSound = false) => {
    if (!video.current) return;
    if (enableSound && !audioBlocked) { setSound(!sound); video.current.muted = sound; }
    try { await video.current.play(); } catch { setStatus("Playback was blocked. Press Play again to allow it."); }
  };
  const toggleFullscreen = async () => {
    try { if (document.fullscreenElement === panel.current) await document.exitFullscreen(); else await panel.current?.requestFullscreen(); }
    catch { setStatus("Fullscreen is unavailable in this browser window."); }
  };
  return <div className={`stream-card${chrome.visible ? "" : " fullscreen-chrome-hidden"}`} ref={panel} data-stream-id={streamId} data-focused={focused} data-watching={watch} {...chrome.surfaceProps}>
    <div className="stream-heading stream-overlay stream-overlay-top" {...chrome.overlayProps}><strong>{nickname}</strong><span className={`stream-badge${status === "Playing" ? " is-live" : ""}`}>{status === "Playing" ? "Live" : "Stream"}</span>{onFocusStream && <button className="icon-button focus-control" aria-label={focused ? "Return to grid" : "Focus stream"} title={focused ? "Return to grid" : "Focus stream"} aria-pressed={focused} onClick={onFocusStream}><Icon name={focused ? "grid" : "focus"} /></button>}</div>
    <div className="video-surface"><video ref={video} autoPlay playsInline muted={audioBlocked || !sound} aria-label={`${nickname}'s stream`} onPlaying={() => setStatus("Playing")} onWaiting={() => watch && setStatus("Waiting for frames")} />{!watch && <div className="stream-placeholder" aria-hidden="true"><Icon name="eye" /></div>}{onFocusStream && !fullscreen && <button className="video-focus-target" aria-label={focused ? `Return ${nickname}'s stream to grid` : `Focus ${nickname}'s stream`} title={focused ? "Return to grid" : "Focus stream"} onClick={onFocusStream} />}</div>
    <div className="stream-overlay stream-overlay-bottom" {...chrome.overlayProps}>
    <div className={`stream-status${error || status.includes("Press Play") ? " needs-attention" : ""}`} role="status">{status}</div>
    {audioBlocked && <div className="stream-status needs-attention" role="status">Room sound is muted to prevent recapture. Stop system audio sharing to restore sound, or use application audio.</div>}
    <div className="stream-controls">
      <button className="icon-button watch-control" aria-label={watch ? "Stop watching" : "Watch stream"} title={watch ? "Stop watching" : "Watch stream"} onClick={() => setWatch(!watch)}><Icon name={watch ? "stop" : "eye"} /></button>
      {watch && <><button className="icon-button" aria-label="Play" title="Play" onClick={() => { void play(); }}><Icon name="play" /></button><button className="icon-button" disabled={audioBlocked} aria-label={sound && !audioBlocked ? "Mute" : "Enable sound"} title={audioBlocked ? "Stop system audio sharing to restore room sound." : sound ? "Mute" : "Enable sound"} onClick={() => { void play(true); }}><Icon name={sound && !audioBlocked ? "sound" : "mute"} /></button></>}
      <label className="volume-control" title={`Volume: ${volume}%`}><span className="sr-only">{nickname}'s volume</span><input type="range" min="0" max="100" step="1" value={volume} onChange={event => setVolume(Number(event.target.value))} aria-valuetext={`${volume}%`} /><output aria-hidden="true">{volume}%</output></label>
      {error && <button className="icon-button" aria-label="Retry" title="Retry" onClick={() => setRetry(value => value + 1)}><Icon name="retry" /></button>}
      <DiagnosticsPanel sample={diagnostics} />
      <ConnectionDiagnostics samples={connectionDiagnostics} />
      <button className="icon-button" aria-label={fullscreen ? "Return to room" : "Fullscreen"} title={fullscreen ? "Return to room" : "Fullscreen"} onClick={() => { void toggleFullscreen(); }}><Icon name="fullscreen" /></button>
    </div>
    </div>
  </div>;
}
