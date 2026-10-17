import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { useFullscreenChrome } from "../src/room/useFullscreenChrome";

function Harness({ fullscreen = false, attention = false, showFirst = true }) {
  const chrome = useFullscreenChrome(fullscreen, attention);
  return <div data-testid="surface" {...chrome.surfaceProps}>
    <output>{chrome.visible ? "visible" : "hidden"}</output>
    <div data-testid="overlay" {...chrome.overlayProps}>{showFirst && <button>First</button>}<button>Last</button></div>
    <button>Outside overlay</button>
  </div>;
}
const visible = () => expect(screen.getByText("visible")).toBeDefined();
const hidden = () => expect(screen.getByText("hidden")).toBeDefined();
const advance = (ms: number) => act(() => vi.advanceTimersByTime(ms));
beforeEach(() => vi.useFakeTimers());
afterEach(() => { cleanup(); vi.useRealTimers(); });

it("EntryHidesDespitePointerButtonFocus", () => {
  const view = render(<Harness />);
  fireEvent.pointerDown(screen.getByRole("button", { name: "First" }));
  act(() => screen.getByRole("button", { name: "First" }).focus());
  view.rerender(<Harness fullscreen />);
  hidden(); advance(2000); hidden();
  expect(document.activeElement).toBe(screen.getByRole("button", { name: "First" }));
  // Hiding pointer targets can itself dispatch pointer-out; it is not renewed user activity.
  fireEvent.pointerOut(screen.getByRole("button", { name: "First" }), { relatedTarget: screen.getByTestId("surface") });
  hidden();
});

it("MovementRevealsAndRestartsIdle", () => {
  render(<Harness fullscreen />); hidden();
  fireEvent.pointerMove(screen.getByTestId("surface")); visible();
  advance(1999); visible();
  fireEvent.pointerMove(screen.getByTestId("surface"));
  advance(1999); visible(); advance(1); hidden();
});

it("LeavingVideoImmediatelyHidesAndClearsIdle", () => {
  render(<Harness fullscreen />);
  const surface = screen.getByTestId("surface");
  fireEvent.pointerMove(surface); advance(500); visible();
  fireEvent.pointerLeave(surface); hidden(); expect(vi.getTimerCount()).toBe(0);
});

it("LeavingHoveredControlClearsHoverAndHidesUntilNewActivity", () => {
  render(<Harness fullscreen />);
  const surface = screen.getByTestId("surface");
  const first = screen.getByRole("button", { name: "First" });
  fireEvent.pointerOver(first); advance(4000); visible();
  fireEvent.pointerOut(first, { relatedTarget: document.body }); hidden();
  expect(vi.getTimerCount()).toBe(0);
  fireEvent.pointerMove(surface); visible(); advance(2000); hidden();
  // Surface exit must also discard a stale overlay hover when no overlay-out was delivered.
  fireEvent.pointerOver(first); visible();
  fireEvent.pointerLeave(surface); hidden(); expect(vi.getTimerCount()).toBe(0);
  fireEvent.pointerMove(surface); visible(); advance(2000); hidden();
});

it("SurfaceExitPreservesPressedControlHoldButReleasesStaleHover", () => {
  render(<Harness fullscreen />);
  const first = screen.getByRole("button", { name: "First" });
  fireEvent.pointerOver(first); fireEvent.pointerDown(first);
  fireEvent.pointerOut(first, { relatedTarget: document.body });
  advance(4000); visible(); expect(vi.getTimerCount()).toBe(0);
  fireEvent.pointerUp(document); advance(1999); visible(); advance(1); hidden();
});

it("SurfaceExitPreservesKeyboardFocusAndAttentionHolds", () => {
  const view = render(<Harness fullscreen />);
  const surface = screen.getByTestId("surface");
  fireEvent.keyDown(document, { key: "Tab" });
  act(() => screen.getByRole("button", { name: "First" }).focus());
  fireEvent.pointerLeave(surface); advance(4000); visible(); expect(vi.getTimerCount()).toBe(0);
  act(() => screen.getByRole("button", { name: "Outside overlay" }).focus());
  advance(2000); hidden();
  view.rerender(<Harness fullscreen attention />);
  fireEvent.pointerMove(surface); fireEvent.pointerLeave(surface);
  advance(4000); visible(); expect(vi.getTimerCount()).toBe(0);
  view.rerender(<Harness fullscreen />); advance(2000); hidden();
});

it("InteractiveHoverAndKeyboardFocusHoldOpen", () => {
  render(<Harness fullscreen />);
  const first = screen.getByRole("button", { name: "First" });
  fireEvent.pointerOver(first); advance(4000); visible();
  fireEvent.pointerOut(first, { relatedTarget: screen.getByTestId("surface") });
  advance(1999); visible(); advance(1); hidden();
  fireEvent.keyDown(document, { key: "Tab" }); act(() => first.focus()); visible();
  advance(4000); visible();
  fireEvent.keyDown(first, { key: "Tab" }); act(() => screen.getByRole("button", { name: "Last" }).focus());
  advance(4000); visible();
  fireEvent.keyDown(document.activeElement!, { key: "Tab" }); act(() => screen.getByRole("button", { name: "Outside overlay" }).focus());
  advance(1999); visible(); advance(1); hidden();
});

it("TabRecoversHiddenControlsThroughActualFocusTraversal", async () => {
  vi.useRealTimers();
  const user = userEvent.setup();
  render(<Harness fullscreen />); hidden();
  await user.tab();
  visible(); expect(document.activeElement).toBe(screen.getByRole("button", { name: "First" }));
});

it("OverlayBackgroundDoesNotHoldChromeOpen", () => {
  render(<Harness fullscreen />);
  fireEvent.pointerOver(screen.getByTestId("overlay"));
  fireEvent.pointerMove(screen.getByTestId("overlay")); advance(2000); hidden();
});

it("ControlPointerDownHoldsUntilReleasedAndFocusExitResumesIdle", () => {
  render(<Harness fullscreen />);
  const first = screen.getByRole("button", { name: "First" });
  fireEvent.pointerDown(first); act(() => first.focus()); advance(4000); visible();
  fireEvent.pointerUp(document); advance(1999); visible(); advance(1); hidden();
  fireEvent.keyDown(document, { key: "Tab" });
  act(() => screen.getByRole("button", { name: "Last" }).focus()); advance(4000); visible();
  act(() => screen.getByRole("button", { name: "Outside overlay" }).focus());
  advance(2000); hidden();
});

it("TouchAndErrorsRevealControls", () => {
  const view = render(<Harness fullscreen />);
  fireEvent.touchStart(screen.getByTestId("surface")); visible(); advance(2000); hidden();
  view.rerender(<Harness fullscreen attention />); visible(); advance(8000); visible();
  view.rerender(<Harness fullscreen />); advance(1999); visible(); advance(1); hidden();
});

it("RemovedKeyboardControlReleasesFocusHoldWhenAttentionResolves", () => {
  const view = render(<Harness fullscreen attention />);
  fireEvent.keyDown(document, { key: "Tab" });
  act(() => screen.getByRole("button", { name: "First" }).focus());
  advance(4000); visible();
  view.rerender(<Harness fullscreen showFirst={false} />);
  expect(document.activeElement).toBe(document.body);
  advance(1999); visible(); advance(1); hidden();
});

it("MovementReconcilesFocusAfterKeyboardControlRemovalWithoutBlur", () => {
  const view = render(<Harness fullscreen />);
  fireEvent.keyDown(document, { key: "Tab" });
  act(() => screen.getByRole("button", { name: "First" }).focus());
  advance(4000); visible();
  view.rerender(<Harness fullscreen showFirst={false} />);
  expect(document.activeElement).toBe(document.body);
  fireEvent.pointerMove(screen.getByTestId("surface"));
  advance(1999); visible(); advance(1); hidden();
});

it("ExitAndUnmountClearTimers", () => {
  const view = render(<Harness fullscreen />);
  fireEvent.pointerMove(screen.getByTestId("surface")); expect(vi.getTimerCount()).toBe(1);
  view.rerender(<Harness />); visible(); expect(vi.getTimerCount()).toBe(0);
  fireEvent.keyDown(document, { key: "Tab" }); expect(vi.getTimerCount()).toBe(0);
  view.rerender(<Harness fullscreen />); hidden();
  fireEvent.pointerMove(screen.getByTestId("surface")); view.unmount();
  expect(vi.getTimerCount()).toBe(0);
});
