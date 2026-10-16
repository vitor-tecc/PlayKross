import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StreamPlayer } from "../src/room/StreamPlayer";
import type { PlaybackGrant } from "../src/api/contracts";
import { readFileSync } from "node:fs";
// Vitest strips CSS imports; load the real rules to exercise opacity and pointer hit testing.
const styles = readFileSync("src/style.css", "utf8");

const mocks = vi.hoisted(() => ({ sessions: [] as { start: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn> }[], media: { getTracks: () => [] } }));
vi.mock("../src/media/WhepSession", () => ({ WhepSession: class {
  start = vi.fn(async () => mocks.media); close = vi.fn(async () => {});
  getPeer = () => null;
  constructor() { mocks.sessions.push(this); }
} }));
beforeEach(() => {
  const style = document.createElement("style"); style.textContent = styles; document.head.append(style);
  mocks.sessions.length = 0;
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(); vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
  Object.defineProperty(document, "fullscreenElement", { configurable: true, value: null });
  Object.defineProperty(HTMLElement.prototype, "requestFullscreen", { configurable: true, value: vi.fn(async function(this: HTMLElement) { Object.defineProperty(document, "fullscreenElement", { configurable: true, value: this }); document.dispatchEvent(new Event("fullscreenchange")); }) });
  document.exitFullscreen = vi.fn(async () => { Object.defineProperty(document, "fullscreenElement", { configurable: true, value: null }); document.dispatchEvent(new Event("fullscreenchange")); });
});
afterEach(() => { cleanup(); document.head.querySelectorAll("style").forEach(style => style.remove()); vi.restoreAllMocks(); });
it("FullscreenReturnsToRoomAndRenamePreservesThePlayingStream", async () => {
  const user = userEvent.setup(); const getPlayback = vi.fn(async () => ({ streamId: "stream" }) as PlaybackGrant);
  const view = render(<StreamPlayer streamId="stream" nickname="Alice" getPlayback={getPlayback} />);
  await user.click(screen.getByRole("button", { name: "Watch stream" }));
  const video = view.container.querySelector("video")!;
  await waitFor(() => expect(video.srcObject).toBe(mocks.media));
  await user.click(screen.getByRole("button", { name: "Fullscreen" }));
  fireEvent.pointerMove(view.container.querySelector(".stream-card")!);
  await user.click(screen.getByRole("button", { name: "Return to room" }));
  expect(video.srcObject).toBe(mocks.media);
  view.rerender(<StreamPlayer streamId="stream" nickname="<script>新名字" getPlayback={getPlayback} />);
  expect(screen.getByText("<script>新名字")).toBeDefined(); expect(view.container.querySelector("script")).toBeNull();
  expect(mocks.sessions).toHaveLength(1); expect(mocks.sessions[0]!.close).not.toHaveBeenCalled();
  view.unmount(); expect(video.srcObject).toBeNull(); expect(mocks.sessions[0]!.close).toHaveBeenCalledOnce();
});

it("OtherCardIsUnaffectedByFullscreenChromeAndSoundIsPreserved", async () => {
  const user = userEvent.setup();
  const getPlayback = vi.fn(async (streamId: string) => ({ streamId }) as PlaybackGrant);
  const view = render(<><StreamPlayer streamId="a" nickname="Alice" getPlayback={getPlayback} /><StreamPlayer streamId="b" nickname="Bob" getPlayback={getPlayback} /></>);
  const [a, b] = view.container.querySelectorAll<HTMLElement>(".stream-card");
  for (const card of [a!, b!]) await user.click(within(card).getByRole("button", { name: "Watch stream" }));
  const video = a!.querySelector("video")!;
  await waitFor(() => expect(video.srcObject).toBe(mocks.media));
  await user.click(within(a!).getByRole("button", { name: "Enable sound" }));
  await user.click(within(a!).getByRole("button", { name: "Fullscreen" }));
  vi.useFakeTimers();
  try {
    expect(a!.classList.contains("fullscreen-chrome-hidden")).toBe(true);
    expect(b!.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    fireEvent.pointerMove(a!);
    expect(a!.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    act(() => vi.advanceTimersByTime(2000));
    expect(a!.classList.contains("fullscreen-chrome-hidden")).toBe(true);
    fireEvent.keyDown(document, { key: "Tab" });
    expect(a!.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    fireEvent.click(within(a!).getByRole("button", { name: "Return to room" }));
    expect(a!.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    expect(a!.querySelector("video")).toBe(video); expect(video.srcObject).toBe(mocks.media); expect(video.muted).toBe(false);
    expect(mocks.sessions).toHaveLength(2);
    for (const session of mocks.sessions) { expect(session.start).toHaveBeenCalledOnce(); expect(session.close).not.toHaveBeenCalled(); }
    expect(getPlayback).toHaveBeenCalledTimes(2);
  } finally { vi.useRealTimers(); }
});

it("HiddenOverlaysRejectPointerClicksUntilMovementRevealsThem", async () => {
  const user = userEvent.setup();
  const view = render(<StreamPlayer streamId="a" nickname="Alice" getPlayback={vi.fn()} />);
  const card = view.container.querySelector<HTMLElement>(".stream-card")!;
  await user.click(screen.getByRole("button", { name: "Fullscreen" }));
  const back = screen.getByRole("button", { name: "Return to room" });
  expect(getComputedStyle(card).cursor).toBe("none");
  for (const overlay of card.querySelectorAll(".stream-overlay")) {
    expect(getComputedStyle(overlay).opacity).toBe("0");
    expect(getComputedStyle(overlay).pointerEvents).toBe("none");
  }
  await expect(user.click(back)).rejects.toThrow(/pointer-events/);
  expect(document.fullscreenElement).toBe(card);
  fireEvent.pointerMove(card);
  await user.click(back);
  expect(getComputedStyle(card).cursor).not.toBe("none");
});

it("BlockedPlayRequiresAttentionButRoutinePlaybackStatusDoesNot", async () => {
  const user = userEvent.setup();
  vi.mocked(HTMLMediaElement.prototype.play).mockRejectedValueOnce(new Error("autoplay blocked"));
  const getPlayback = vi.fn(async () => ({ streamId: "a" }) as PlaybackGrant);
  const view = render(<StreamPlayer streamId="a" nickname="Alice" getPlayback={getPlayback} />);
  await user.click(screen.getByRole("button", { name: "Watch stream" }));
  await screen.findByText("Connected. Press Play to begin playback.");
  await user.click(screen.getByRole("button", { name: "Fullscreen" }));
  const card = view.container.querySelector<HTMLElement>(".stream-card")!;
  expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(false);
  vi.useFakeTimers();
  try {
    act(() => vi.advanceTimersByTime(4000));
    expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    fireEvent.playing(card.querySelector("video")!);
    act(() => vi.advanceTimersByTime(2000));
    expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(true);
    fireEvent.waiting(card.querySelector("video")!);
    expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(true);
  } finally { vi.useRealTimers(); }
});

it("KeyboardRetryRemovalReleasesFullscreenFocusHold", async () => {
  const user = userEvent.setup();
  const grant: PlaybackGrant = { streamId: "a", path: "rooms/room/streams/a/main", whepUri: "/media/rooms/room/streams/a/main/whep", token: "grant-a", expiresAt: "later" };
  const getPlayback = vi.fn(async () => grant);
  for (let attempt = 0; attempt < 4; attempt++) getPlayback.mockRejectedValueOnce(new Error("Playback unavailable"));
  const view = render(<StreamPlayer streamId="a" nickname="Alice" getPlayback={getPlayback} />);
  const card = view.container.querySelector<HTMLElement>(".stream-card")!;
  vi.useFakeTimers();
  try {
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Watch stream" })); });
    for (const delay of [1000, 2000, 4000]) await act(async () => { await vi.advanceTimersByTimeAsync(delay); });
    const retry = screen.getByRole("button", { name: "Retry" });
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Fullscreen" })); });
    fireEvent.keyDown(document, { key: "Tab" }); act(() => retry.focus());
    expect(document.activeElement).toBe(retry);
    // Testing Library's keyboard wrapper needs real timers in this Vitest environment.
    vi.useRealTimers();
    await user.keyboard("{Enter}");
    expect(screen.queryByRole("button", { name: "Retry" })).toBeNull();
    expect(document.activeElement).toBe(document.body);
    expect(card.querySelector("video")!.srcObject).toBe(mocks.media);
    // Movement cancels the real idle timer and starts a deterministic fake-clock deadline.
    vi.useFakeTimers({ shouldClearNativeTimers: true });
    fireEvent.pointerMove(card);
    act(() => vi.advanceTimersByTime(1999));
    expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(false);
    act(() => vi.advanceTimersByTime(1));
    expect(card.classList.contains("fullscreen-chrome-hidden")).toBe(true);
  } finally { vi.useRealTimers(); }
});

it("StoppingOneCardKeepsTheOtherCardPlayingAcrossRename", async () => {
  const user = userEvent.setup();
  const getPlayback = vi.fn(async (streamId: string) => ({ streamId, path: `rooms/room/streams/${streamId}/main`, whepUri: `/media/rooms/room/streams/${streamId}/main/whep`, token: `grant-${streamId}`, expiresAt: "later" }));
  const cards = (nickname: string) => <><StreamPlayer streamId="a" nickname="Alice" getPlayback={getPlayback} /><StreamPlayer streamId="b" nickname={nickname} getPlayback={getPlayback} /></>;
  const view = render(cards("Bob"));
  const panels = view.container.querySelectorAll<HTMLElement>(".stream-card");
  for (const panel of panels) await user.click(within(panel).getByRole("button", { name: "Watch stream" }));
  const [aVideo, bVideo] = view.container.querySelectorAll("video");
  await waitFor(() => { expect(aVideo!.srcObject).toBe(mocks.media); expect(bVideo!.srcObject).toBe(mocks.media); });
  await user.click(within(panels[0]!).getByRole("button", { name: "Stop watching" }));
  view.rerender(cards("Renamed Bob"));
  expect(aVideo!.srcObject).toBeNull(); expect(bVideo!.srcObject).toBe(mocks.media);
  expect(screen.getByText("Renamed Bob")).toBeDefined();
  expect(mocks.sessions).toHaveLength(2); expect(mocks.sessions[0]!.close).toHaveBeenCalledOnce(); expect(mocks.sessions[1]!.close).not.toHaveBeenCalled();
});
