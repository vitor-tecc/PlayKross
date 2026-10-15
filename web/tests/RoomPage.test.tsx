import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { RoomPage } from "../src/room/RoomPage";
const api = vi.hoisted(() => ({ me: vi.fn(), join: vi.fn(), rename: vi.fn(), leave: vi.fn(), playback: vi.fn(), watchRoom: vi.fn(async () => async () => {}) }));
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
it("LeavingClosesTheSubscriptionBeforeReleasingMembershipAndShowsTheJoinForm", async () => {
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: { roomId: "room", revision: 1, participants: [{ sessionId: "me", nickname: "friend" }], streams: [] } });
  let finishClose!: () => void;
  const closed = new Promise<void>(resolve => { finishClose = resolve; });
  api.watchRoom.mockResolvedValueOnce(async () => closed);
  api.leave.mockResolvedValue({ roomId: "room", revision: 2, participants: [], streams: [] });
  render(<RoomPage />);
  fireEvent.click(await screen.findByRole("button", { name: "Leave room" }));
  expect(api.leave).not.toHaveBeenCalled();
  finishClose();
  await waitFor(() => expect(api.leave).toHaveBeenCalledWith(expect.any(AbortSignal)));
  expect(await screen.findByRole("heading", { name: "Join your friends" })).toBeDefined();
  expect(screen.queryByText("friend · you")).toBeNull();
});
