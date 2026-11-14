import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { PlaybackGrant, PublishReservation, RoomSnapshot, SessionGrant, SessionOverview } from "./contracts";
export type RoomLinkState = "connected" | "reconnecting" | "closed";
export class RoomApiError extends Error {
  constructor(public readonly status: number) { super(status === 401 || status === 403 ? "Your room session is unavailable. Open the invitation link and join again." : status === 429 ? "Too many join attempts. Wait a minute before retrying." : "Room request failed. Check the connection and nickname, then retry."); }
}
async function request<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, credentials: "same-origin" });
  if (!response.ok) throw new RoomApiError(response.status);
  return response.json() as Promise<T>;
}
export const roomApi = {
  me: (signal: AbortSignal) => request<SessionOverview>("/api/me", { signal }),
  join: (invite: string, nickname: string, signal: AbortSignal) => request<SessionGrant>("/api/join", { method: "POST", signal, headers: { "Content-Type": "application/json" }, body: JSON.stringify({ invite, nickname }) }),
  rename: (nickname: string, signal: AbortSignal) => request<RoomSnapshot>("/api/me/nickname", { method: "POST", signal, headers: { "Content-Type": "application/json" }, body: JSON.stringify({ nickname }) }),
  leave: (signal: AbortSignal) => request<RoomSnapshot>("/api/me/leave", { method: "POST", signal }),
  playback: (id: string, signal: AbortSignal) => request<PlaybackGrant>(`/api/streams/${encodeURIComponent(id)}/playback`, { signal }),
  reservePublication: (signal: AbortSignal) => request<PublishReservation>("/api/streams?requireNew=true", { method: "POST", signal }),
  renewPublication: (id: string, signal: AbortSignal) => request<RoomSnapshot>(`/api/streams/${encodeURIComponent(id)}/lease`, { method: "POST", signal }),
  stopPublication: (id: string, signal: AbortSignal, keepalive = false) => request<RoomSnapshot>(`/api/streams/${encodeURIComponent(id)}`, { method: "DELETE", signal, keepalive }),
  /**
   * Subscribes to room snapshots. The host sends a full snapshot on every
   * (re)connection, so no update is lost across automatic reconnects. When
   * automatic reconnection gives up, `connection("closed")` lets the caller
   * open a fresh subscription.
   */
  watchRoom: async (changed: (snapshot: RoomSnapshot) => void, signal: AbortSignal, connection?: (state: RoomLinkState) => void) => {
    const hub = new HubConnectionBuilder().withUrl("/hubs/room").configureLogging(LogLevel.None).withAutomaticReconnect([1000, 2000, 4000]).build();
    hub.on("room", changed);
    if (connection) {
      hub.onreconnecting(() => { if (!signal.aborted) connection("reconnecting"); });
      hub.onreconnected(() => { if (!signal.aborted) connection("connected"); });
      hub.onclose(() => { if (!signal.aborted) connection("closed"); });
    }
    const stop = () => { void hub.stop(); }; signal.addEventListener("abort", stop, { once: true });
    try { await hub.start(); if (signal.aborted) await hub.stop(); }
    catch (error) { signal.removeEventListener("abort", stop); await hub.stop(); throw error; }
    return async () => { signal.removeEventListener("abort", stop); hub.off("room", changed); await hub.stop(); };
  },
};
