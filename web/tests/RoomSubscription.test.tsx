import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { RoomSnapshot } from "../src/api/contracts";
import type { RoomLinkState } from "../src/api/roomApi";
import { RoomPage } from "../src/room/RoomPage";
vi.mock("../src/media/WhipSession", () => ({ WhipSession: class { start = vi.fn(async () => {}); close = vi.fn(async () => {}); } }));
const api = vi.hoisted(() => ({ me: vi.fn(), join: vi.fn(), rename: vi.fn(), leave: vi.fn(), playback: vi.fn(), reservePublication: vi.fn(), renewPublication: vi.fn(), stopPublication: vi.fn(),
  watchRoom: vi.fn(async (_changed: (room: RoomSnapshot) => void, _signal: AbortSignal, _connection?: (state: RoomLinkState) => void) => async () => {}) }));
vi.mock("../src/api/roomApi", () => ({ roomApi: api, RoomApiError: class extends Error { status = 401; } }));
afterEach(() => { cleanup(); vi.useRealTimers(); sessionStorage.clear(); vi.clearAllMocks(); });
const interrupted = "Room updates were interrupted. Reconnecting…";
const room = (revision: number, ...others: string[]): RoomSnapshot => ({ roomId: "room", revision, participants: [{ sessionId: "me", nickname: "friend" }, ...others.map(name => ({ sessionId: name, nickname: name }))], streams: [] });

it("ARoomSubscriptionThatGivesUpIsReopenedAndShowsReconnectingMeanwhile", async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: room(1) });
  let connection!: (state: RoomLinkState) => void;
  api.watchRoom.mockImplementationOnce(async (_changed, _signal, state) => { connection = state!; return async () => {}; });
  render(<RoomPage />);
  await screen.findByRole("button", { name: "Leave room" });
  await waitFor(() => expect(api.watchRoom).toHaveBeenCalledOnce());
  await act(async () => connection("reconnecting"));
  expect(screen.getByText(interrupted)).toBeDefined();
  await act(async () => connection("connected"));
  expect(screen.queryByText(interrupted)).toBeNull();
  await act(async () => connection("closed"));
  expect(screen.getByText(interrupted)).toBeDefined();
  expect(api.watchRoom).toHaveBeenCalledOnce();
  await act(async () => { await vi.advanceTimersByTimeAsync(2000); });
  expect(api.watchRoom).toHaveBeenCalledTimes(2);
  await waitFor(() => expect(screen.queryByText(interrupted)).toBeNull());
});

it("AFailedResubscriptionRetriesWithBackoffAndStopsAfterLeaving", async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: room(1) });
  api.leave.mockResolvedValue(room(2));
  api.watchRoom.mockRejectedValueOnce(new Error("offline")).mockRejectedValueOnce(new Error("offline"));
  render(<RoomPage />);
  expect(await screen.findByText(interrupted)).toBeDefined();
  await act(async () => { await vi.advanceTimersByTimeAsync(2000); });
  expect(api.watchRoom).toHaveBeenCalledTimes(2);
  // The second delay is longer than the first.
  await act(async () => { await vi.advanceTimersByTimeAsync(4000); });
  expect(api.watchRoom).toHaveBeenCalledTimes(2);
  fireEvent.click(screen.getByRole("button", { name: "Leave room" }));
  await waitFor(() => expect(api.leave).toHaveBeenCalled());
  await act(async () => { await vi.advanceTimersByTimeAsync(30000); });
  expect(api.watchRoom).toHaveBeenCalledTimes(2);
});

it("StaleOrDuplicateRoomSnapshotsAreIgnored", async () => {
  api.me.mockResolvedValue({ sessionId: "me", expiresAt: "later", room: room(5) });
  let changed!: (room: RoomSnapshot) => void;
  api.watchRoom.mockImplementationOnce(async update => { changed = update; return async () => {}; });
  render(<RoomPage />);
  await screen.findByText("1 in the room");
  await waitFor(() => expect(changed).toBeDefined());
  await act(async () => changed(room(4, "Old")));
  await act(async () => changed(room(5, "Duplicate")));
  expect(screen.getByText("1 in the room")).toBeDefined();
  await act(async () => changed(room(6, "New")));
  expect(screen.getByText("2 in the room")).toBeDefined();
});
