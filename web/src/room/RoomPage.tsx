import { t, useLanguage, LanguageSelector } from "../i18n";
import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import type { RoomSnapshot, SessionGrant, SessionOverview } from "../api/contracts";
import { roomApi, RoomApiError, type RoomLinkState } from "../api/roomApi";
import { StreamPlayer } from "./StreamPlayer";
import { useHostPlaybackAudioBlocked } from "../media/hostPlaybackAudio";
import { flushSync } from "react-dom";
import { BrowserPublisher, type PublisherSnapshot } from "../media/BrowserPublisher";
import { WhipSession } from "../media/WhipSession";
import { BroadcastControls } from "./BroadcastControls";

const inviteStorageKey = "playkross-invite";
const idlePublisher: PublisherSnapshot = { phase: "idle", streamId: null, hasAudio: false, error: null };
/** Delays before re-opening a room subscription after SignalR's own reconnects give up. */
export const ROOM_RESUBSCRIBE_MS = [2000, 5000, 10000, 15000] as const;
const captureAudioReason = "Room sound is muted to prevent captured-audio feedback. Stop sharing to restore room sound.";
const unavailableReason = () => !window.isSecureContext || !navigator.mediaDevices?.getDisplayMedia ? "Screen sharing needs HTTPS (or localhost) and a browser supporting display capture." : null;
const errorMessage = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback;
const toOverview = (joined: SessionGrant): SessionOverview => ({ sessionId: joined.sessionId, expiresAt: joined.expiresAt, room: joined.room });

function readInvitation() {
  const hash = new URLSearchParams(location.hash.slice(1));
  return { invite: hash.get("invite") ?? sessionStorage.getItem(inviteStorageKey) ?? "", explicit: hash.has("invite") };
}
/** Keeps the invitation for reloads in this tab and removes it from the visible URL. */
function rememberInvitation(invite: string) {
  sessionStorage.setItem(inviteStorageKey, invite);
  history.replaceState(null, "", location.pathname + location.search);
}

export function RoomPage() {
  useLanguage();
  const hostAudioBlocked = useHostPlaybackAudioBlocked();
  const [captureAudioBlocked, setCaptureAudioBlocked] = useState(false);
  const audioBlocked = hostAudioBlocked || captureAudioBlocked;
  const audioBlockedReason = hostAudioBlocked ? undefined : captureAudioReason;
  const [publisherState, setPublisherState] = useState<PublisherSnapshot>(idlePublisher);
  const publisher = useRef<BrowserPublisher | null>(null);
  const [{ invite, explicit: explicitInvitation }] = useState(readInvitation);
  const [session, setSession] = useState<SessionOverview | null>(null); const [nickname, setNickname] = useState("");
  const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); const [loading, setLoading] = useState(true);
  const [roomLink, setRoomLink] = useState<RoomLinkState>("connected");
  const [focusedStream, setFocusedStream] = useState<string | null>(null);
  const streams = session?.room.streams ?? [];
  const activeFocus = focusedStream !== publisherState.streamId && streams.some(stream => stream.streamId === focusedStream) ? focusedStream : null;
  useEffect(() => { if (focusedStream && !activeFocus) setFocusedStream(null); }, [focusedStream, activeFocus]);
  const subscription = useRef<{ stop: () => Promise<void> } | null>(null);
  // Ignore stale or duplicate snapshots: revisions only grow and identify content.
  const apply = useCallback((room: RoomSnapshot) => setSession(previous => previous && previous.room.roomId === room.roomId && previous.room.revision < room.revision ? { ...previous, room } : previous), []);

  useEffect(() => {
    if (!session) return;
    let active = true;
    setPublisherState(idlePublisher);
    const controller = new BrowserPublisher({ secureContext: window.isSecureContext === true, capture: navigator.mediaDevices?.getDisplayMedia?.bind(navigator.mediaDevices), api: roomApi,
      createTransport: failed => new WhipSession(undefined, failed), now: () => performance.now(), sessionExpiresAt: session.expiresAt,
      // Muting must reach mounted players before the capture chooser opens.
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
        known = toOverview(await roomApi.join(invite, name, abort.signal));
        if (!abort.signal.aborted) rememberInvitation(invite);
      }
      if (!abort.signal.aborted) { setSession(known); setNickname(name); }
    }).catch(error => { if (!abort.signal.aborted && !(error instanceof RoomApiError && error.status === 401)) setMessage(error.message); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [invite, explicitInvitation]);

  useEffect(() => {
    if (!session) return;
    const abort = new AbortController();
    let attempt: Promise<(() => Promise<void>) | undefined> = Promise.resolve(undefined);
    let retry: ReturnType<typeof setTimeout> | null = null;
    let failures = 0;
    const resubscribeLater = () => {
      if (abort.signal.aborted || retry) return;
      setRoomLink("reconnecting");
      retry = setTimeout(() => { retry = null; subscribe(); }, ROOM_RESUBSCRIBE_MS[Math.min(failures++, ROOM_RESUBSCRIBE_MS.length - 1)]);
    };
    // A "closed" hub has already stopped, so a new subscription replaces it without awaiting its close.
    const subscribe = () => {
      if (abort.signal.aborted) return;
      attempt = roomApi.watchRoom(apply, abort.signal, state => { if (abort.signal.aborted) return; if (state === "closed") resubscribeLater(); else setRoomLink(state); })
        .then(close => { if (!abort.signal.aborted) { failures = 0; setRoomLink("connected"); } return close; })
        .catch(() => { resubscribeLater(); return undefined; });
    };
    subscribe();
    let stopping: Promise<void> | null = null;
    const watching = { stop: () => {
      abort.abort();
      if (retry) { clearTimeout(retry); retry = null; }
      stopping ??= attempt.then(close => close?.());
      return stopping;
    } };
    subscription.current = watching;
    return () => { if (subscription.current === watching) subscription.current = null; setRoomLink("connected"); void watching.stop().catch(() => {}); };
  }, [session?.sessionId, apply]);

  const enter = (joined: SessionGrant) => { setSession(toOverview(joined)); rememberInvitation(invite); };
  const join = async () => {
    if (!invite) { setMessage("Open the room invitation link supplied by the host."); return; }
    setBusy(true); setMessage("");
    try { enter(await roomApi.join(invite, nickname, new AbortController().signal)); }
    catch (error) { setMessage(errorMessage(error, "Could not join the room.")); }
    finally { setBusy(false); }
  };
  const rename = async () => {
    setBusy(true); setMessage("");
    try { apply(await roomApi.rename(nickname, new AbortController().signal)); }
    catch (error) { setMessage(errorMessage(error, "Nickname update failed.")); }
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
    } catch (error) { setSession(previous); setMessage(errorMessage(error, "Could not leave the room. Try again.")); }
    finally { setBusy(false); }
  };
  const copyLink = async () => {
    try { await navigator.clipboard.writeText(location.origin + "/#invite=" + encodeURIComponent(invite)); setMessage("Room link copied."); }
    catch { setMessage("Clipboard unavailable. Copy the original invitation link from the host."); }
  };

  const you = session?.room.participants.find(participant => participant.sessionId === session.sessionId);
  return <main className="app-shell">
    <header className="topbar">
      <div className="brand-lockup"><img className="brand-logo" src="/playkross-logo.png" alt="" /><div><div className="brand">PlayKross</div><p>{t("Share a screen. Stay in the room.")}</p></div></div>
      <div className="header-actions">{session && <span className="room-count">{session.room.participants.length}{t(" in the room")}</span>}<LanguageSelector /></div>
    </header>
    <div className="notices">
      {message && <div className="notice" role="status">{t(message)}</div>}
      {session && roomLink !== "connected" && <div className="notice notice-warning" role="status">{t("Room updates were interrupted. Reconnecting…")}</div>}
    </div>
    {loading ? <p className="loading"><span className="spinner" aria-hidden="true" />{t("Restoring your room session…")}</p> : !session ? <section className="join-view">
      <div className="join-hero">
        <img className="join-logo" src="/playkross-logo.png" alt="" />
        <h1 className="join-title">Play<span>Kross</span></h1>
        <p className="join-lead">{t("Share a screen. Stay in the room.")}</p>
        <ul className="join-features">
          <li>{t("Several friends can broadcast at the same time.")}</li>
          <li>{t("Watch any stream, focus one, or go fullscreen.")}</li>
          <li>{t("Share from the browser or the Windows app.")}</li>
        </ul>
      </div>
      <section className="join-card"><h2>{t("Join your friends")}</h2><p>{t("Choose a nickname. No account needed.")}</p><form onSubmit={event => { event.preventDefault(); void join(); }}><label htmlFor="nickname">{t("Nickname")}</label><input id="nickname" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} autoComplete="nickname" required autoFocus /><button disabled={busy} type="submit">{t(busy ? "Joining…" : "Join room")}</button></form></section>
    </section> : <div className="room-layout">
      <section className="stage" aria-label={t("Room streams")}>
        <div className="streams-toolbar"><h1>{t("Streams")} <span className="count-pill">{streams.length}</span></h1>{activeFocus && <button onClick={() => setFocusedStream(null)}>{t("Grid view")}</button>}</div>
        <div className={`room-grid${activeFocus ? " room-grid-focused" : ""}`} style={{ "--focus-rows": Math.max(1, streams.length - 1) } as CSSProperties}>{streams.map(stream => stream.streamId === publisherState.streamId ?
          <div key={stream.streamId} className="stream-card own-broadcast" data-stream-id={stream.streamId} data-focused="false"><strong>{t("Your screen is being shared")}</strong><p>{t("Your viewers receive this broadcast. Use Stop sharing to end it.")}</p></div> :
          <StreamPlayer key={stream.streamId} streamId={stream.streamId} nickname={stream.nickname} getPlayback={getPlayback} audioBlocked={audioBlocked} audioBlockedReason={audioBlockedReason} focused={activeFocus === stream.streamId} onFocusStream={() => setFocusedStream(current => current === stream.streamId ? null : stream.streamId)} />)}{streams.length === 0 && <div className="empty-room"><img src="/playkross-logo.png" alt="" /><h2>{t("The room is ready")}</h2><p>{t("Start sharing here or broadcast from the Windows app.")}</p></div>}</div>
      </section>
      <aside className="sidebar">
        <section className="panel profile-panel">
          <div className="profile-head"><span className="avatar" aria-hidden="true">{Array.from((you?.nickname ?? nickname).trim())[0]?.toUpperCase() ?? "?"}</span><form className="room-toolbar" onSubmit={event => { event.preventDefault(); void rename(); }}><label htmlFor="rename">{t("Your nickname")}</label><div className="inline-field"><input id="rename" value={nickname} onChange={event => setNickname(event.target.value)} maxLength={1024} required /><button disabled={busy}>{t("Save")}</button></div></form></div>
          <div className="profile-actions">{invite && <button onClick={() => { void copyLink(); }}>{t("Copy room link")}</button>}<button className="button-quiet" disabled={busy} onClick={() => { void leave(); }}>{t("Leave room")}</button></div>
        </section>
        <BroadcastControls state={publisherState} unavailableReason={unavailableReason()} onStart={(audio, quality) => { void publisher.current?.start(audio, quality); }} onStop={() => { void publisher.current?.stop(); }} />
        <section className="panel people-panel"><h2>{t("In the room")} <span className="count-pill">{session.room.participants.length}</span></h2>
          <ul className="people">{session.room.participants.map(participant => <li className={`participant${participant.sessionId === session.sessionId ? " is-you" : ""}`} data-initial={Array.from(participant.nickname.trim())[0]?.toUpperCase() ?? "?"} key={participant.sessionId}>{participant.nickname}{participant.sessionId === session.sessionId ? t(" · you") : ""}</li>)}</ul>
        </section>
      </aside>
    </div>}
  </main>;
}
