import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { RoomPage } from "../src/room/RoomPage";
const api = vi.hoisted(() => ({ me: vi.fn(), join: vi.fn(), rename: vi.fn(), playback: vi.fn(), watchRoom: vi.fn(async () => async () => {}) }));
vi.mock("../src/api/roomApi", () => ({ roomApi: api, RoomApiError: class extends Error { status = 401; } }));
afterEach(() => { cleanup(); sessionStorage.clear(); history.replaceState(null, "", "/"); vi.clearAllMocks(); });
it("AnExplicitInvitationSelectsItsRoomInsteadOfRestoringAnotherRoom", async () => {
  history.replaceState(null, "", "/#invite=new-invitation");
  api.me.mockResolvedValue({ sessionId: "old-session", expiresAt: "later", room: { roomId: "old-room", revision: 1, participants: [{ sessionId: "old-session", nickname: "friend" }], streams: [] } });
  api.join.mockResolvedValue({ sessionId: "new-session", expiresAt: "later", room: { roomId: "new-room", revision: 1, participants: [{ sessionId: "new-session", nickname: "friend" }, { sessionId: "target", nickname: "Target room broadcaster" }], streams: [] } });
  render(<RoomPage />);
  await waitFor(() => expect(api.join).toHaveBeenCalledWith("new-invitation", "friend", expect.any(AbortSignal)));
  expect(await screen.findByText("Target room broadcaster")).toBeDefined();
});
