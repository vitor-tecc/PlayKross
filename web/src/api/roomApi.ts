import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import type { PlaybackGrant, PublishReservation, RoomSnapshot, SessionGrant, SessionOverview } from "./contracts";
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
  watchRoom: async (changed: (snapshot: RoomSnapshot) => void, signal: AbortSignal) => {
    const hub = new HubConnectionBuilder().withUrl("/hubs/room").configureLogging(LogLevel.None).withAutomaticReconnect([1000, 2000, 4000]).build();
    hub.on("room", changed);
    const stop = () => { void hub.stop(); }; signal.addEventListener("abort", stop, { once: true });
    try { await hub.start(); if (signal.aborted) await hub.stop(); }
    catch (error) { signal.removeEventListener("abort", stop); await hub.stop(); throw error; }
    return async () => { signal.removeEventListener("abort", stop); hub.off("room", changed); await hub.stop(); };
  },
};
