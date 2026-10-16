import { describe, expect, it } from "vitest";
import { summarizeTimingLatency } from "../src/media/timingMarker";

describe("sustained timing evidence", () => {
  it("retains a delayed tail instead of certifying only the fast frames", () => {
    const summary = summarizeTimingLatency([...Array<number>(570).fill(100), ...Array<number>(30).fill(3000)], 0, 600);
    // The 95th-percentile nearest rank is fast at exactly 5%; use 6% delayed.
    const delayed = summarizeTimingLatency([...Array<number>(564).fill(100), ...Array<number>(36).fill(3000)], 0, 600);
    expect(summary.latencyP95Ms).toBe(100);
    expect(delayed.latencyP95Ms).toBe(3000);
  });
  it("refuses ten good observations in a mostly unreadable ten-minute run", () => {
    const result = summarizeTimingLatency(Array<number>(10).fill(100), 590, 600);
    expect(result.latencyP95Ms).toBeNull();
    expect(result.latencyCoveragePercent).toBeCloseTo(1.6666667);
  });
  it("counts time without callbacks against coverage", () => {
    expect(summarizeTimingLatency(Array<number>(10).fill(100), 0, 600).latencyP95Ms).toBeNull();
  });
  it("accepts dense observations but refuses invalid numeric evidence", () => {
    expect(summarizeTimingLatency(Array<number>(597).fill(201), 0, 600).latencyP95Ms).toBe(201);
    expect(summarizeTimingLatency([NaN, ...Array<number>(599).fill(100)], 0, 600).latencyP95Ms).toBeNull();
    expect(summarizeTimingLatency([100], 0, Infinity).latencyP95Ms).toBeNull();
  });
});
