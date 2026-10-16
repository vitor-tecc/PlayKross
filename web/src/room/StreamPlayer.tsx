import type { PlaybackGrant } from "../api/contracts";
import { useEffect, useRef, useState } from "react";
import { WhepSession } from "../media/WhepSession";
import { observePlayer, type ViewerDiagnostics } from "../media/playerDiagnostics";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { useFullscreenChrome } from "./useFullscreenChrome";
export interface StreamPlayerProps { streamId: string; nickname: string; getPlayback: (id: string, signal: AbortSignal) => Promise<PlaybackGrant>; audioBlocked?: boolean }
export function StreamPlayer({ streamId, nickname, getPlayback, audioBlocked = false }: StreamPlayerProps) {
  const panel = useRef<HTMLDivElement>(null); const video = useRef<HTMLVideoElement>(null);
  const [watch, setWatch] = useState(false); const [retry, setRetry] = useState(0); const [sound, setSound] = useState(false);
  const [status, setStatus] = useState("Ready to watch"); const [error, setError] = useState(false); const [fullscreen, setFullscreen] = useState(false);
  const [diagnostics, setDiagnostics] = useState<ViewerDiagnostics | null>(null);
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
      const session = new WhepSession(undefined, reason => { if (current === session) schedule(reason, attempt + 1); }); current = session;
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
  return <div className={`stream-card${chrome.visible ? "" : " fullscreen-chrome-hidden"}`} ref={panel} data-stream-id={streamId} {...chrome.surfaceProps}>
    <div className="stream-heading stream-overlay stream-overlay-top" {...chrome.overlayProps}><strong>{nickname}</strong><span>{status === "Playing" ? "Live" : "Stream"}</span></div>
    <div className="video-surface"><video ref={video} autoPlay playsInline muted={audioBlocked || !sound} aria-label={`${nickname}'s stream`} onPlaying={() => setStatus("Playing")} onWaiting={() => watch && setStatus("Waiting for frames")} /></div>
    <div className="stream-overlay stream-overlay-bottom" {...chrome.overlayProps}>
    <div className="stream-status" role="status">{status}</div>
    {audioBlocked && <div className="stream-status" role="status">Room sound is muted to prevent recapture. Stop system audio sharing to restore sound, or use application audio.</div>}
    <div className="stream-controls">
      <button onClick={() => setWatch(!watch)}>{watch ? "Stop watching" : "Watch stream"}</button>
      {watch && <><button onClick={() => { void play(); }}>Play</button><button disabled={audioBlocked} title={audioBlocked ? "Stop system audio sharing to restore room sound." : undefined} onClick={() => { void play(true); }}>{sound && !audioBlocked ? "Mute" : "Enable sound"}</button></>}
      {error && <button onClick={() => setRetry(value => value + 1)}>Retry</button>}
      <button onClick={() => { void toggleFullscreen(); }}>{fullscreen ? "Return to room" : "Fullscreen"}</button>
    </div>
    <DiagnosticsPanel sample={diagnostics} />
    </div>
  </div>;
}
