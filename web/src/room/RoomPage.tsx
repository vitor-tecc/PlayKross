import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import type { RoomSnapshot, SessionOverview } from "../api/contracts";
import { roomApi, RoomApiError } from "../api/roomApi";
import { StreamPlayer } from "./StreamPlayer";
import { useHostPlaybackAudioBlocked } from "../media/hostPlaybackAudio";
export function RoomPage() {
  const audioBlocked = useHostPlaybackAudioBlocked();
  const [invite] = useState(() => new URLSearchParams(location.hash.slice(1)).get("invite") ?? sessionStorage.getItem("jk-invite") ?? "");
  const [explicitInvitation] = useState(() => new URLSearchParams(location.hash.slice(1)).has("invite"));
  const [session, setSession] = useState<SessionOverview | null>(null); const [nickname, setNickname] = useState("");
  const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); const [loading, setLoading] = useState(true);
  const [focusedStream, setFocusedStream] = useState<string | null>(null);
  const activeFocus = session?.room.streams.some(stream => stream.streamId === focusedStream) ? focusedStream : null;
  useEffect(() => { if (focusedStream && !activeFocus) setFocusedStream(null); }, [focusedStream, activeFocus]);
  const subscription = useRef<{ stop: () => Promise<void> } | null>(null);
  const apply = useCallback((room: RoomSnapshot) => setSession(previous => previous && previous.room.roomId === room.roomId && previous.room.revision <= room.revision ? { ...previous, room } : previous), []);
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
    setBusy(true); setMessage(""); setSession(null);
    try {
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
      <footer className="room-footer"><span>In the room</span>{session.room.participants.map(participant => <span className="participant" key={participant.sessionId}>{participant.nickname}{participant.sessionId === session.sessionId ? " · you" : ""}</span>)}</footer>
      <div className="streams-toolbar"><h1>Streams <span>{session.room.streams.length}</span></h1>{activeFocus && <button onClick={() => setFocusedStream(null)}>Grid view</button>}</div>
      <section className={`room-grid${activeFocus ? " room-grid-focused" : ""}`} style={{ "--focus-rows": Math.max(1, session.room.streams.length - 1) } as CSSProperties} aria-label="Room streams">{session.room.streams.map(stream => <StreamPlayer key={stream.streamId} streamId={stream.streamId} nickname={stream.nickname} getPlayback={getPlayback} audioBlocked={audioBlocked} focused={activeFocus === stream.streamId} onFocusStream={() => setFocusedStream(current => current === stream.streamId ? null : stream.streamId)} />)}{session.room.streams.length === 0 && <div className="empty-room"><h1>The room is ready</h1><p>Start a broadcast from the Windows app. Viewers can stay in this browser.</p></div>}</section>
    </>}
  </main>;
}
