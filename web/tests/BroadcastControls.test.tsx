import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { BroadcastControls } from "../src/room/BroadcastControls";
afterEach(cleanup);
it("CapturedAudioDefaultsOffAndCaptureRequiresAButtonGesture", () => {
  const start = vi.fn(), stop = vi.fn();
  const view = render(<BroadcastControls state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} unavailableReason={null} onStart={start} onStop={stop} />);
  expect((screen.getByLabelText("Share captured audio") as HTMLInputElement).checked).toBe(false); expect(start).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" })); expect(start).toHaveBeenCalledWith(false);
  view.rerender(<BroadcastControls state={{ phase: "choosing", streamId: null, hasAudio: false, error: null }} unavailableReason={null} onStart={start} onStop={stop} />);
  fireEvent.click(screen.getByRole("button", { name: "Stop sharing" })); expect(stop).toHaveBeenCalledOnce();
});
it("UnsupportedCaptureIsExplainedAndUnavailableAudioIsDisclosed", () => {
  const props = { onStart: vi.fn(), onStop: vi.fn() };
  const view = render(<BroadcastControls {...props} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} unavailableReason="Use HTTPS and a supported browser." />);
  expect((screen.getByRole("button", { name: "Start sharing" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/Use HTTPS/)).toBeDefined();
  fireEvent.click(screen.getByLabelText("Share captured audio"));
  view.rerender(<BroadcastControls {...props} unavailableReason={null} state={{ phase: "live", streamId: "owned", hasAudio: false, error: null }} />);
  expect(screen.getByText(/Video only.*unavailable|Video only.*ended/)).toBeDefined();
});
