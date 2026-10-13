import type { PlaybackGrant } from "../api/contracts";
import { useEffect, useRef, useState } from "react";
import { WhepSession } from "../media/WhepSession";
export interface StreamPlayerProps { streamId: string; nickname: string; getPlayback: (id: string, signal: AbortSignal) => Promise<PlaybackGrant> }
export function StreamPlayer({ streamId, nickname, getPlayback }: StreamPlayerProps) {
  const panel = useRef<HTMLDivElement>(null); const video = useRef<HTMLVideoElement>(null);
  const [watch, setWatch] = useState(false); const [retry, setRetry] = useState(0); const [sound, setSound] = useState(false);
  const [status, setStatus] = useState("Ready to watch"); const [error, setError] = useState(false); const [fullscreen, setFullscreen] = useState(false);
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === panel.current);
    document.addEventListener("fullscreenchange", changed); return () => document.removeEventListener("fullscreenchange", changed);
  }, []);
  useEffect(() => {
    if (!watch) { setStatus("Ready to watch"); setError(false); return; }
    const abort = new AbortController(); const element = video.current;
    let current: WhepSession | null = null; let timer: ReturnType<typeof setTimeout> | null = null;
    const detach = () => { if (element) { element.pause(); element.srcObject = null; } };
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
    if (enableSound) { setSound(!sound); video.current.muted = sound; }
    try { await video.current.play(); } catch { setStatus("Playback was blocked. Press Play again to allow it."); }
  };
  const toggleFullscreen = async () => {
    try { if (document.fullscreenElement === panel.current) await document.exitFullscreen(); else await panel.current?.requestFullscreen(); }
    catch { setStatus("Fullscreen is unavailable in this browser window."); }
  };
  return <div className="stream-card" ref={panel} data-stream-id={streamId}>
    <div className="stream-heading"><strong>{nickname}</strong><span>{status === "Playing" ? "Live" : "Stream"}</span></div>
    <div className="video-surface"><video ref={video} autoPlay playsInline muted={!sound} aria-label={`${nickname}'s stream`} onPlaying={() => setStatus("Playing")} onWaiting={() => watch && setStatus("Waiting for frames")} /></div>
    <div className="stream-status" role="status">{status}</div>
    <div className="stream-controls">
      <button onClick={() => setWatch(!watch)}>{watch ? "Stop watching" : "Watch stream"}</button>
      {watch && <><button onClick={() => { void play(); }}>Play</button><button onClick={() => { void play(true); }}>{sound ? "Mute" : "Enable sound"}</button></>}
      {error && <button onClick={() => setRetry(value => value + 1)}>Retry</button>}
      <button onClick={() => { void toggleFullscreen(); }}>{fullscreen ? "Return to room" : "Fullscreen"}</button>
    </div>
  </div>;
}
