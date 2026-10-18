import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import type { RoomSnapshot, SessionOverview } from "../api/contracts";
import { roomApi, RoomApiError } from "../api/roomApi";
import { StreamPlayer } from "./StreamPlayer";
import { useHostPlaybackAudioBlocked } from "../media/hostPlaybackAudio";
import { flushSync } from "react-dom";
import { BrowserPublisher, type PublisherSnapshot } from "../media/BrowserPublisher";
import { WhipSession } from "../media/WhipSession";
import { BroadcastControls } from "./BroadcastControls";
export function RoomPage() {
  const hostAudioBlocked = useHostPlaybackAudioBlocked();
  const [captureAudioBlocked, setCaptureAudioBlocked] = useState(false);
  const audioBlocked = hostAudioBlocked || captureAudioBlocked;
  const audioBlockedReason = hostAudioBlocked ? undefined : "Room sound is muted to prevent captured-audio feedback. Stop sharing to restore room sound.";
  const [publisherState, setPublisherState] = useState<PublisherSnapshot>({ phase: "idle", streamId: null, hasAudio: false, error: null });
  const publisher = useRef<BrowserPublisher | null>(null);
  const unavailableReason = !window.isSecureContext || !navigator.mediaDevices?.getDisplayMedia ? "Screen sharing needs HTTPS (or localhost) and a browser supporting display capture." : null;
  const [invite] = useState(() => new URLSearchParams(location.hash.slice(1)).get("invite") ?? sessionStorage.getItem("jk-invite") ?? "");
  const [explicitInvitation] = useState(() => new URLSearchParams(location.hash.slice(1)).has("invite"));
  const [session, setSession] = useState<SessionOverview | null>(null); const [nickname, setNickname] = useState("");
  const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); const [loading, setLoading] = useState(true);
  const [focusedStream, setFocusedStream] = useState<string | null>(null);
  const activeFocus = focusedStream !== publisherState.streamId && session?.room.streams.some(stream => stream.streamId === focusedStream) ? focusedStream : null;
  useEffect(() => { if (focusedStream && !activeFocus) setFocusedStream(null); }, [focusedStream, activeFocus]);
  const subscription = useRef<{ stop: () => Promise<void> } | null>(null);
  const apply = useCallback((room: RoomSnapshot) => setSession(previous => previous && previous.room.roomId === room.roomId && previous.room.revision <= room.revision ? { ...previous, room } : previous), []);
  useEffect(() => {
    if (!session) return;
    let active = true;
    setPublisherState({ phase: "idle", streamId: null, hasAudio: false, error: null });
    const controller = new BrowserPublisher({ secureContext: window.isSecureContext === true, capture: navigator.mediaDevices?.getDisplayMedia?.bind(navigator.mediaDevices), api: roomApi,
      createTransport: failed => new WhipSession(undefined, failed), now: () => performance.now(), sessionExpiresAt: session.expiresAt,
      setCaptureAudioBlocked: blocked => { if (blocked) flushSync(() => setCaptureAudioBlocked(true)); else setCaptureAudioBlocked(false); },
    }, value => { if (active) setPublisherState(value); });
    publisher.current = controller;
    const departure = () => { void controller.stop(); };
    window.addEventListener("pagehide", departure);
    return () => { active = false; if (publisher.current === controller) publisher.current = null; window.removeEventListener("pagehide", departure); void controller.stop(); };
  }, [session?.sessionId, session?.expiresAt]);
  useEffect(() => { if (session) publisher.current?.reconcile(session.room); }, [session?.room]);
  useEffect(() => {
    const abort = new AbortController();
    void roomApi.me(abort.signal).then(async known => {
      const name = known.room.participants.find(participant => participant.sessionId === known.sessionId)?.nickname ?? "";
      if (explicitInvitation) {
        const joined = await roomApi.join(invite, name, abort.signal);
        known = { sessionId: joined.sessionId, expiresAt: joined.expiresAt, room: joined.room };
        if (!abort.signal.aborted) { sessionStorage.setItem("jk-invite", invite); history.replaceState(null, "", location.pathname + location.search); }
      }
      if (!abort.signal.aborted) { setSession(known); setNickname(name); }
    }).catch(error => { if (!abort.signal.aborted && !(error instanceof RoomApiError && error.status === 401)) setMessage(error.message); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [invite, explicitInvitation]);
  useEffect(() => {
    if (!session) return;
    const abort = new AbortController();
    const ready = roomApi.watchRoom(apply, abort.signal).catch(error => { if (!abort.signal.aborted) setMessage(error.message); });
    let stopping: Promise<void> | null = null;
    const watching = { stop: () => {
      abort.abort();
      stopping ??= ready.then(close => close?.());
      return stopping;
    } };
    subscription.current = watching;
    return () => { if (subscription.current === watching) subscription.current = null; void watching.stop().catch(() => {}); };
  }, [session?.sessionId, apply]);
  const join = async () => {
    if (!invite) { setMessage("Open the room invitation link supplied by the host."); return; }
    setBusy(true); setMessage("");
    try {
      const joined = await roomApi.join(invite, nickname, new AbortController().signal);
      setSession({ sessionId: joined.sessionId, expiresAt: joined.expiresAt, room: joined.room }); sessionStorage.setItem("jk-invite", invite);
      history.replaceState(null, "", location.pathname + location.search);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Could not join the room."); }
    finally { setBusy(false); }
  };
  const rename = async () => {
    setBusy(true); setMessage("");
    try { apply(await roomApi.rename(nickname, new AbortController().signal)); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Nickname update failed."); }
    finally { setBusy(false); }
  };
  const getPlayback = useCallback((id: string, signal: AbortSignal) => roomApi.playback(id, signal), []);
  const leave = async () => {
    const previous = session; const watching = subscription.current;
    setBusy(true); setMessage("");
    try {
      await publisher.current?.stop();
      setSession(null);
      await watching?.stop();
      await roomApi.leave(new AbortController().signal);
      setMessage("You left the room.");
    } catch (error) { setSession(previous); setMessage(error instanceof Error ? error.message : "Could not leave the room. Try again."); }
    finally { setBusy(false); }
  };
  const copyLink = async () => {
    try { await navigator.clipboard.writeText(location.origin + "/#invite=" + encodeURIComponent(invite)); setMessage("Room link copied."); }
    catch { setMessage("Clipboard unavailable. Copy the original invitation link from the host."); }
  };
  return <main className="room-shell">
    <header className="room-header"><div className="brand-lockup"><img className="brand-logo" src="/playkross-logo.png" alt="" /><div><div className="brand">PlayKross</div><p>Share a screen. Stay in the room.</p></div></div>{session && <span className="room-count">{session.room.participants.length} in the room</span>}</header>
    {message && <div className="notice" role="status">{message}</div>}
    {loading ? <p>Restoring your room session…</p> : !session ? <section className="join-card"><h1>Join your friends</h1><p>Choose a nickname. No account needed.</p><form onSubmit={event => { event.preventDefault(); void join(); }}><label htmlFor="nickname">Nickname</label><input id="nickname" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} autoComplete="nickname" required autoFocus /><button disabled={busy} type="submit">{busy ? "Joining…" : "Join room"}</button></form></section> : <>
      <section className="room-toolbar"><form onSubmit={event => { event.preventDefault(); void rename(); }}><label htmlFor="rename">Your nickname</label><input id="rename" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} required /><button disabled={busy}>Save</button></form>{invite && <button onClick={() => { void copyLink(); }}>Copy room link</button>}<button disabled={busy} onClick={() => { void leave(); }}>Leave room</button></section>
      <BroadcastControls state={publisherState} unavailableReason={unavailableReason} onStart={audio => { void publisher.current?.start(audio); }} onStop={() => { void publisher.current?.stop(); }} />
      <footer className="room-footer"><span>In the room</span>{session.room.participants.map(participant => <span className="participant" key={participant.sessionId}>{participant.nickname}{participant.sessionId === session.sessionId ? " · you" : ""}</span>)}</footer>
      <div className="streams-toolbar"><h1>Streams <span>{session.room.streams.length}</span></h1>{activeFocus && <button onClick={() => setFocusedStream(null)}>Grid view</button>}</div>
      <section className={`room-grid${activeFocus ? " room-grid-focused" : ""}`} style={{ "--focus-rows": Math.max(1, session.room.streams.length - 1) } as CSSProperties} aria-label="Room streams">{session.room.streams.map(stream => stream.streamId === publisherState.streamId ?
        <div key={stream.streamId} className="stream-card own-broadcast" data-stream-id={stream.streamId} data-focused="false"><strong>Your screen is being shared</strong><p>Your viewers receive this broadcast. Stop sharing using the controls above.</p></div> :
        <StreamPlayer key={stream.streamId} streamId={stream.streamId} nickname={stream.nickname} getPlayback={getPlayback} audioBlocked={audioBlocked} audioBlockedReason={audioBlockedReason} focused={activeFocus === stream.streamId} onFocusStream={() => setFocusedStream(current => current === stream.streamId ? null : stream.streamId)} />)}{session.room.streams.length === 0 && <div className="empty-room"><h1>The room is ready</h1><p>Start sharing here or broadcast from the Windows app.</p></div>}</section>
    </>}
  </main>;
}
