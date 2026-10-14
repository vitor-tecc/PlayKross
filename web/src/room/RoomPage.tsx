import { useCallback, useEffect, useState } from "react";
import type { RoomSnapshot, SessionOverview } from "../api/contracts";
import { roomApi, RoomApiError } from "../api/roomApi";
import { StreamPlayer } from "./StreamPlayer";
export function RoomPage() {
  const [invite] = useState(() => new URLSearchParams(location.hash.slice(1)).get("invite") ?? sessionStorage.getItem("jk-invite") ?? "");
  const [explicitInvitation] = useState(() => new URLSearchParams(location.hash.slice(1)).has("invite"));
  const [session, setSession] = useState<SessionOverview | null>(null); const [nickname, setNickname] = useState("");
  const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); const [loading, setLoading] = useState(true);
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
    const abort = new AbortController(); let stop: (() => Promise<void>) | null = null;
    void roomApi.watchRoom(apply, abort.signal).then(close => { stop = close; }).catch(error => { if (!abort.signal.aborted) setMessage(error.message); });
    return () => { abort.abort(); if (stop) void stop(); };
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
  const copyLink = async () => {
    try { await navigator.clipboard.writeText(location.origin + "/#invite=" + encodeURIComponent(invite)); setMessage("Room link copied."); }
    catch { setMessage("Clipboard unavailable. Copy the original invitation link from the host."); }
  };
  return <main className="room-shell">
    <header className="room-header"><div className="brand-lockup"><img className="brand-logo" src="/playkross-logo.png" alt="" /><div><div className="brand">PlayKross</div><p>Share a screen. Stay in the room.</p></div></div>{session && <span className="room-count">{session.room.participants.length} / 8 in the room</span>}</header>
    {message && <div className="notice" role="status">{message}</div>}
    {loading ? <p>Restoring your room session…</p> : !session ? <section className="join-card"><h1>Join your friends</h1><p>Choose a nickname. No account needed.</p><form onSubmit={event => { event.preventDefault(); void join(); }}><label htmlFor="nickname">Nickname</label><input id="nickname" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} autoComplete="nickname" required autoFocus /><button disabled={busy} type="submit">{busy ? "Joining…" : "Join room"}</button></form></section> : <>
      <section className="room-toolbar"><form onSubmit={event => { event.preventDefault(); void rename(); }}><label htmlFor="rename">Your nickname</label><input id="rename" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} required /><button disabled={busy}>Save</button></form>{invite && <button onClick={() => { void copyLink(); }}>Copy room link</button>}</section>
      <section className="room-grid" aria-label="Room streams">{session.room.streams.map(stream => <StreamPlayer key={stream.streamId} streamId={stream.streamId} nickname={stream.nickname} getPlayback={getPlayback} />)}{session.room.streams.length === 0 && <div className="empty-room"><h1>The room is ready</h1><p>Start a broadcast from the Windows app. Viewers can stay in this browser.</p></div>}</section>
      <footer className="room-footer"><span>In the room</span>{session.room.participants.map(participant => <span className="participant" key={participant.sessionId}>{participant.nickname}{participant.sessionId === session.sessionId ? " · you" : ""}</span>)}</footer>
    </>}
  </main>;
}
