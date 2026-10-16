import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StreamPlayer } from "../src/room/StreamPlayer";
import type { PlaybackGrant } from "../src/api/contracts";

const mocks = vi.hoisted(() => ({ sessions: [] as { start: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn> }[], media: { getTracks: () => [] } }));
vi.mock("../src/media/WhepSession", () => ({ WhepSession: class {
  start = vi.fn(async () => mocks.media); close = vi.fn(async () => {});
  getPeer = () => null;
  constructor() { mocks.sessions.push(this); }
} }));
beforeEach(() => {
  mocks.sessions.length = 0;
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(); vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
  Object.defineProperty(document, "fullscreenElement", { configurable: true, value: null });
  Object.defineProperty(HTMLElement.prototype, "requestFullscreen", { configurable: true, value: vi.fn(async function(this: HTMLElement) { Object.defineProperty(document, "fullscreenElement", { configurable: true, value: this }); document.dispatchEvent(new Event("fullscreenchange")); }) });
  document.exitFullscreen = vi.fn(async () => { Object.defineProperty(document, "fullscreenElement", { configurable: true, value: null }); document.dispatchEvent(new Event("fullscreenchange")); });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("FullscreenReturnsToRoomAndRenamePreservesThePlayingStream", async () => {
  const user = userEvent.setup(); const getPlayback = vi.fn(async () => ({ streamId: "stream" }) as PlaybackGrant);
  const view = render(<StreamPlayer streamId="stream" nickname="Alice" getPlayback={getPlayback} />);
  await user.click(screen.getByRole("button", { name: "Watch stream" }));
  const video = view.container.querySelector("video")!;
  await waitFor(() => expect(video.srcObject).toBe(mocks.media));
  await user.click(screen.getByRole("button", { name: "Fullscreen" }));
  await user.click(screen.getByRole("button", { name: "Return to room" }));
  expect(video.srcObject).toBe(mocks.media);
  view.rerender(<StreamPlayer streamId="stream" nickname="<script>新名字" getPlayback={getPlayback} />);
  expect(screen.getByText("<script>新名字")).toBeDefined(); expect(view.container.querySelector("script")).toBeNull();
  expect(mocks.sessions).toHaveLength(1); expect(mocks.sessions[0]!.close).not.toHaveBeenCalled();
  view.unmount(); expect(video.srcObject).toBeNull(); expect(mocks.sessions[0]!.close).toHaveBeenCalledOnce();
});
