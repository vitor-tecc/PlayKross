import { expect, it } from "vitest";
import { decodeTimingMarker } from "../src/media/timingMarker";
it("SyntheticClockStripPreservesUnsignedBitsAndRejectsMissingMarkers", () => {
  const width = 576, height = 1, pixels = new Uint8ClampedArray(width * 4);
  const fill = (start: number, end: number, color: number[]) => { for (let x = start; x < end; x++) pixels.set([...color, 255], x * 4); };
  fill(16, 32, [255, 0, 255]); fill(560, 576, [255, 0, 255]);
  const clock = 0xa7fe0321;
  for (let bit = 0; bit < 32; bit++) fill(48 + bit * 16, 64 + bit * 16, clock & (1 << bit) ? [255, 255, 255] : [0, 0, 0]);
  expect(decodeTimingMarker(pixels, width, height)).toBe(clock);
  expect(decodeTimingMarker(new Uint8ClampedArray(pixels.length), width, height)).toBeNull();
});
