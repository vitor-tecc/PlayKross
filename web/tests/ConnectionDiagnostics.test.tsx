import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { ConnectionDiagnostics } from "../src/room/ConnectionDiagnostics";
import type { WhepDiagnostic } from "../src/media/WhepSession";

afterEach(() => { cleanup(); Reflect.deleteProperty(window, "playkrossPlaybackAudioBlocked"); });
it("MakesFailureEvidenceSelectableWhenEmbeddedViewerBlocksDownloads", () => {
  Object.defineProperty(window, "playkrossPlaybackAudioBlocked", { value: false, configurable: true });
  const sample: WhepDiagnostic = { at: "2026-10-17T19:00:00.000Z", outcome: "failed", stage: "post", failure: "stream-unavailable", elapsedMs: 15020, timingsMs: { gathering: 15000, post: 10 }, optionsStatus: 204, postStatus: 404, gatheringState: "gathering", iceState: "new", connectionState: "new", candidates: { host: 2, srflx: 1, prflx: 0, relay: 0 }, grantRemainingMs: 44990 };
  render(<ConnectionDiagnostics samples={[sample]} />);
  expect(screen.queryByRole("button", { name: "Download connection report" })).toBeNull();
  fireEvent.click(screen.getByText("Show report"));
  const report = screen.getByRole("textbox", { name: "Connection report" }) as HTMLTextAreaElement;
  const parsed = JSON.parse(report.value);
  expect(parsed.samples[0]).toMatchObject({ failure: "stream-unavailable", optionsStatus: 204, postStatus: 404, timingsMs: { gathering: 15000 } });
  fireEvent.focus(report);
  expect(report.selectionStart).toBe(0); expect(report.selectionEnd).toBe(report.value.length);
});
