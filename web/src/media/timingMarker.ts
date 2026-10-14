// Used only by synthetic acceptance tests. No real screen images are persisted.
export function summarizeTimingLatency(latencies: readonly number[], missing: number, durationSeconds: number) {
  const expectedMarkerSamples = Number.isFinite(durationSeconds) && durationSeconds > 0 ? Math.max(1, Math.floor(durationSeconds)) : null;
  const valid = latencies.every(value => Number.isFinite(value) && value >= 0) && Number.isInteger(missing) && missing >= 0;
  const denominator = expectedMarkerSamples === null ? null : Math.max(expectedMarkerSamples, latencies.length + missing);
  const latencyCoveragePercent = valid && denominator !== null ? latencies.length * 100 / denominator : null;
  // Keep ALL valid decoded ages, including multi-second delays. Missing markers
  // and freezes cannot earn a percentile from a handful of good observations.
  const sorted = [...latencies].sort((a, b) => a - b);
  const latencyP95Ms = valid && latencyCoveragePercent !== null && latencyCoveragePercent >= 95 && sorted.length > 0
    ? sorted[Math.ceil(sorted.length * .95) - 1]! : null;
  return { latencyP95Ms, latencyCoveragePercent, expectedMarkerSamples, validMarkerSamples: latencies.length };
}

export function decodeTimingMarker(pixels: Uint8ClampedArray, width: number, height: number): number | null {
  if (width <= 0 || height <= 0 || pixels.length !== width * height * 4) return null;
  for (let y = 0; y < Math.min(height, 200); y += 2) {
    const runs: { start: number; end: number }[] = []; let start = -1;
    for (let x = 0; x <= width; x++) {
      const index = (y * width + x) * 4;
      const magenta = x < width && pixels[index]! > 190 && pixels[index + 1]! < 70 && pixels[index + 2]! > 190;
      if (magenta && start < 0) start = x;
      if (!magenta && start >= 0) { if (x - start >= 3 && x - start <= 40) runs.push({ start, end: x }); start = -1; }
    }
    for (let i = 0; i < runs.length - 1; i++) {
      const left = runs[i]!, right = runs[i + 1]!, a = (left.start + left.end - 1) / 2, b = (right.start + right.end - 1) / 2;
      const pitch = (b - a) / 34;
      if (pitch < 3 || pitch > 40 || Math.abs((left.end - left.start) - pitch) > pitch / 2 || Math.abs((right.end - right.start) - pitch) > pitch / 2) continue;
      let clock = 0, valid = true;
      for (let bit = 0; bit < 32; bit++) {
        const index = (y * width + Math.round(a + (bit + 2) * pitch)) * 4;
        const luma = (pixels[index]! + pixels[index + 1]! + pixels[index + 2]!) / 3;
        if (luma > 150) clock |= 1 << bit; else if (luma > 100) { valid = false; break; }
      }
      if (valid) return clock >>> 0;
    }
  }
  return null;
}
