import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { useHostPlaybackAudioBlocked } from "../src/media/hostPlaybackAudio";

afterEach(() => { cleanup(); delete window.playkrossPlaybackAudioBlocked; });
it("ReadsInitialHostFlagAndTracksBooleanUpdates", () => {
  window.playkrossPlaybackAudioBlocked = true;
  const hook = renderHook(useHostPlaybackAudioBlocked); expect(hook.result.current).toBe(true);
  act(() => window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: false })));
  expect(hook.result.current).toBe(false);
  act(() => window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: "false" })));
  expect(hook.result.current).toBe(false);
});
it("ExternalBrowserDefaultsToUnblockedAndUnsubscribes", () => {
  const hook = renderHook(useHostPlaybackAudioBlocked); expect(hook.result.current).toBe(false); hook.unmount();
  act(() => window.dispatchEvent(new CustomEvent("playkross-playback-audio-blocked", { detail: true })));
  expect(hook.result.current).toBe(false);
});
