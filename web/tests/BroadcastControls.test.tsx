import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { BroadcastControls } from "../src/room/BroadcastControls";
afterEach(cleanup);
it("CapturedAudioDefaultsOffAndCaptureRequiresAButtonGesture", () => {
  const start = vi.fn(), stop = vi.fn();
  const view = render(<BroadcastControls state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} unavailableReason={null} onStart={start} onStop={stop} />);
  expect((screen.getByLabelText("Share captured audio") as HTMLInputElement).checked).toBe(false); expect(start).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" })); expect(start).toHaveBeenCalledWith(false, { width: 1280, height: 720, fps: 30, bitrateMbps: 4 });
  view.rerender(<BroadcastControls state={{ phase: "choosing", streamId: null, hasAudio: false, error: null }} unavailableReason={null} onStart={start} onStop={stop} />);
  fireEvent.click(screen.getByRole("button", { name: "Stop sharing" })); expect(stop).toHaveBeenCalledOnce();
});
it("QualityPresetsAndCustomBitrateReachStartAndAreLockedWhileSharing", () => {
  const start = vi.fn(), props = { unavailableReason: null, onStart: start, onStop: vi.fn() };
  const view = render(<BroadcastControls {...props} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} />);
  fireEvent.change(screen.getByLabelText("Resolution"), { target: { value: "1080" } });
  expect((screen.getByLabelText("Video bitrate (Mbps)") as HTMLInputElement).value).toBe("8");
  fireEvent.change(screen.getByLabelText("Frame rate"), { target: { value: "60" } });
  expect((screen.getByLabelText("Video bitrate (Mbps)") as HTMLInputElement).value).toBe("15");
  fireEvent.change(screen.getByLabelText("Video bitrate (Mbps)"), { target: { value: "12.5" } });
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" }));
  expect(start).toHaveBeenCalledWith(false, { width: 1920, height: 1080, fps: 60, bitrateMbps: 12.5 });
  view.rerender(<BroadcastControls {...props} state={{ phase: "live", streamId: "owned", hasAudio: false, error: null }} />);
  for (const label of ["Resolution", "Frame rate", "Video bitrate (Mbps)"]) expect((screen.getByLabelText(label) as HTMLInputElement).disabled).toBe(true);
});
it.each([[480, 30, 854, 2], [480, 60, 854, 3], [720, 30, 1280, 4], [720, 60, 1280, 6], [1080, 30, 1920, 8], [1080, 60, 1920, 15]])("Preset %ip%i passes the desktop quality to Start", (height, fps, width, bitrateMbps) => {
  const start = vi.fn(); render(<BroadcastControls unavailableReason={null} onStart={start} onStop={vi.fn()} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} />);
  fireEvent.change(screen.getByLabelText("Resolution"), { target: { value: String(height) } });
  fireEvent.change(screen.getByLabelText("Frame rate"), { target: { value: String(fps) } });
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" }));
  expect(start).toHaveBeenCalledWith(false, { width, height, fps, bitrateMbps });
});
it.each(["", "0.5", "25.5", "1.25"])("Invalid bitrate %s disables Start", value => {
  const start = vi.fn(); render(<BroadcastControls unavailableReason={null} onStart={start} onStop={vi.fn()} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} />);
  fireEvent.change(screen.getByLabelText("Video bitrate (Mbps)"), { target: { value } });
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" })); expect(start).not.toHaveBeenCalled();
});
it("UploadDiagnosticsDistinguishActualBitrateFromTheSelectedLimit", () => {
  render(<BroadcastControls unavailableReason={null} onStart={vi.fn()} onStop={vi.fn()} state={{ phase: "live", streamId: "owned", hasAudio: false, error: null,
    stats: { bitrateMbps: 0.5, width: 1280, height: 720, fps: 24, limitation: "bandwidth" } }} />);
  expect(screen.getByText("0.50 Mbps")).toBeDefined(); expect(screen.getByText("1280 × 720")).toBeDefined(); expect(screen.getByText("24 FPS")).toBeDefined();
  expect(screen.getByText("Network bandwidth")).toBeDefined();
});
it("UnsupportedCaptureIsExplainedAndUnavailableAudioIsDisclosed", () => {
  const props = { onStart: vi.fn(), onStop: vi.fn() };
  const view = render(<BroadcastControls {...props} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} unavailableReason="Use HTTPS and a supported browser." />);
  expect((screen.getByRole("button", { name: "Start sharing" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/Use HTTPS/)).toBeDefined();
  fireEvent.click(screen.getByLabelText("Share captured audio"));
  view.rerender(<BroadcastControls {...props} unavailableReason={null} state={{ phase: "live", streamId: "owned", hasAudio: false, error: null }} />);
  expect(screen.getByText(/Video only.*unavailable|Video only.*ended/)).toBeDefined();
});
it("RemembersTheLastStartedQualityButNeverCapturedAudio", () => {
  const start = vi.fn(), idle = { phase: "idle" as const, streamId: null, hasAudio: false, error: null };
  const first = render(<BroadcastControls unavailableReason={null} onStart={start} onStop={vi.fn()} state={idle} />);
  fireEvent.change(screen.getByLabelText("Resolution"), { target: { value: "1080" } });
  fireEvent.change(screen.getByLabelText("Video bitrate (Mbps)"), { target: { value: "9.5" } });
  fireEvent.click(screen.getByLabelText("Share captured audio"));
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" }));
  first.unmount();
  render(<BroadcastControls unavailableReason={null} onStart={start} onStop={vi.fn()} state={idle} />);
  expect((screen.getByLabelText("Resolution") as HTMLSelectElement).value).toBe("1080");
  expect((screen.getByLabelText("Video bitrate (Mbps)") as HTMLInputElement).value).toBe("9.5");
  expect((screen.getByLabelText("Share captured audio") as HTMLInputElement).checked).toBe(false);
});
it("IgnoresInvalidRememberedQuality", () => {
  localStorage.setItem("playkross-broadcast-quality", JSON.stringify({ width: 4000, height: 2160, fps: 120, bitrateMbps: 80 }));
  const start = vi.fn();
  render(<BroadcastControls unavailableReason={null} onStart={start} onStop={vi.fn()} state={{ phase: "idle", streamId: null, hasAudio: false, error: null }} />);
  fireEvent.click(screen.getByRole("button", { name: "Start sharing" }));
  expect(start).toHaveBeenCalledWith(false, { width: 1280, height: 720, fps: 30, bitrateMbps: 4 });
});
