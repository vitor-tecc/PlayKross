import { test, expect } from "@playwright/test";

// Existing protocol scenarios assert English interface text.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("playkross-language", "en"));
});
import { mkdirSync, writeFileSync } from "node:fs";
import { decodeTimingMarker, summarizeTimingLatency } from "../../src/media/timingMarker";
test("measure sustained synthetic native motion and application audio", async ({ page, browser }) => {
  test.skip(!process.env.JKILLER_SUSTAINED_SECONDS, "Run with Test-Sustained.ps1.");
  const seconds = Number(process.env.JKILLER_SUSTAINED_SECONDS); test.setTimeout((seconds + 60) * 1000);
  const videoOnly = process.env.JKILLER_PROBE_CAPTURE_MODE === "video-only";
  await page.addInitScript(() => {
    const peers: RTCPeerConnection[] = []; (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers = peers;
    const Original = window.RTCPeerConnection;
    window.RTCPeerConnection = class extends Original { constructor(configuration?: RTCConfiguration) { super(configuration); peers.push(this); } };
  });
  const link = new URL(process.env.JKILLER_PROBE_JOIN_LINK!); await page.goto(link.href);
  await page.getByLabel("Nickname", { exact: true }).fill("Sustained synthetic viewer"); await page.getByRole("button", { name: "Join room" }).click();
  await page.getByRole("button", { name: "Watch stream" }).click();
  await expect.poll(() => page.locator("video").evaluate(v => (v as HTMLVideoElement).videoHeight), { timeout: 30000 }).toBe(1080);
  await page.getByRole("button", { name: "Enable sound" }).click();
  await page.waitForTimeout(5000);
  const measured = await page.evaluate(async ({ seconds, decoderSource, videoOnly }) => {
    const video = document.querySelector("video")!;
    const peer = (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers.find(value => value.connectionState === "connected");
    if (!peer) throw new Error("No actual decoder connection.");
    const decodedSample = async () => {
      const stats = await peer.getStats();
      const inbound = [...stats.values()].find(value => value.type === "inbound-rtp" && value.kind === "video");
      return { at: performance.now(), id: inbound?.id, ssrc: inbound?.ssrc, frames: inbound?.framesDecoded as number | undefined };
    };
    const decodedStart = await decodedSample();
    const decode = new Function(`return (${decoderSource})`)() as (pixels: Uint8ClampedArray, width: number, height: number) => number | null;
    const canvas = document.createElement("canvas"); canvas.width = 960; canvas.height = 540; const context = canvas.getContext("2d", { willReadFrequently: true })!;
    const audio = new AudioContext(); await audio.resume(); const input = audio.createMediaStreamSource(video.srcObject as MediaStream);
    const analyser = audio.createAnalyser(); analyser.fftSize = 8192; analyser.smoothingTimeConstant = 0; input.connect(analyser);
    // A browser playback tone is outside the selected native application's process tree.
    const oscillator = audio.createOscillator(), gain = audio.createGain(); oscillator.frequency.value = 2753; gain.gain.value = .014; oscillator.connect(gain).connect(audio.destination); if (!videoOnly) oscillator.start();
    const spectrum = new Float32Array(analyser.frequencyBinCount);
    const levels: Record<string, number> = { "997": -200, "1801": -200, "2753": -200 };
    const began = performance.now(), intervals: number[] = [], latencies: number[] = [], markerReadMs: number[] = [];
    let callback = 0, firstFrames: number | null = null, totalFrames = 0, previousAt: number | null = null, longestGapMs = 0, intervalAt = began, intervalFrames = 0, lastMarker = 0, markersMissing = 0;
    const onFrame: VideoFrameRequestCallback = (now, metadata) => {
      if (firstFrames === null) { firstFrames = metadata.presentedFrames; intervalFrames = metadata.presentedFrames; }
      totalFrames = metadata.presentedFrames - firstFrames;
      if (previousAt !== null) longestGapMs = Math.max(longestGapMs, now - previousAt); previousAt = now;
      if (now - intervalAt >= 5000) { intervals.push((metadata.presentedFrames - intervalFrames) * 1000 / (now - intervalAt)); intervalAt = now; intervalFrames = metadata.presentedFrames; }
      if (now - lastMarker >= 1000) {
        lastMarker = now; const started = performance.now(); context.drawImage(video, 0, 0, 960, 540);
        const decoded = decode(context.getImageData(0, 0, 960, 540).data, 960, 540); markerReadMs.push(performance.now() - started);
        if (decoded !== null) latencies.push(((Date.now() >>> 0) - decoded + 4294967296) % 4294967296); else markersMissing++;
      }
      callback = video.requestVideoFrameCallback(onFrame);
    };
    callback = video.requestVideoFrameCallback(onFrame);
    const audioTimer = setInterval(() => {
      analyser.getFloatFrequencyData(spectrum);
      for (const frequency of [997, 1801, 2753]) {
        const center = Math.round(frequency * analyser.fftSize / audio.sampleRate);
        for (let bin = center - 2; bin <= center + 2; bin++) levels[String(frequency)] = Math.max(levels[String(frequency)]!, spectrum[bin]!);
      }
    }, 50);
    await new Promise(resolve => setTimeout(resolve, seconds * 1000));
    const durationSeconds = (performance.now() - began) / 1000;
    const terminalGap = previousAt === null ? durationSeconds * 1000 : performance.now() - previousAt; longestGapMs = Math.max(longestGapMs, terminalGap);
    video.cancelVideoFrameCallback(callback); clearInterval(audioTimer); if (!videoOnly) oscillator.stop(); await audio.close();
    const decodedEnd = await decodedSample();
    const averageDecodedFps = decodedStart.frames !== undefined && decodedEnd.frames !== undefined && decodedEnd.frames >= decodedStart.frames && decodedEnd.id === decodedStart.id && decodedEnd.ssrc === decodedStart.ssrc
      ? (decodedEnd.frames - decodedStart.frames) * 1000 / (decodedEnd.at - decodedStart.at) : null;
    return { durationSeconds, averageDecodedFps, decodedMeasurementSeconds: (decodedEnd.at - decodedStart.at) / 1000, averagePresentedFps: totalFrames / durationSeconds, healthyFiveSecondPercent: intervals.length ? intervals.filter(fps => fps >= 55).length * 100 / intervals.length : null,
      intervals, longestGapMs, latencies, markersMissing, markerReadMs, spectralPeakDb: levels, width: video.videoWidth, height: video.videoHeight, visible: document.visibilityState === "visible" };
  }, { seconds, decoderSource: decodeTimingMarker.toString(), videoOnly });
  const reports = [];
  const report = await (await page.request.get(new URL("/api/diagnostics", link.origin).href)).json(); reports.push(report);
  mkdirSync("../.artifacts/acceptance", { recursive: true });
  const timing = summarizeTimingLatency(measured.latencies, measured.markersMissing, measured.durationSeconds);
  const { latencyP95Ms } = timing;
  const result = { browser: browser.version(), topology: "loopback-same-PC", workload: "native-60fps-moving-bands-clock-strip", captureMode: videoOnly ? "video-only" : "application", audioValidation: videoOnly ? "pending; application audio disabled" : "synthetic spectrum only; Discord and calibrated sync pending", ...measured, ...timing,
    latencyMethod: "same-machine Windows wall clock in synthetic pixel strip to browser callback; no physical-display or cross-machine clock claim", audioMethod: "8192-bin WebAudio frequency peaks, no waveform recording; controlled 1801Hz native process and 2753Hz browser playback present", reports };
  writeFileSync("../.artifacts/acceptance/sustained-viewer.json", JSON.stringify(result, null, 2));
  console.log(JSON.stringify({ seconds: measured.durationSeconds, presentedFps: measured.averagePresentedFps, healthyPercent: measured.healthyFiveSecondPercent, longestGapMs: measured.longestGapMs, ...timing, missingMarkers: measured.markersMissing, spectralDb: measured.spectralPeakDb }));
  expect(measured.visible).toBe(true); expect(measured.width).toBe(1920); expect(measured.height).toBe(1080); expect(measured.averagePresentedFps).toBeGreaterThanOrEqual(57);
  expect(measured.averageDecodedFps).not.toBeNull(); expect(measured.averageDecodedFps!).toBeGreaterThanOrEqual(57);
  expect(measured.healthyFiveSecondPercent).toBeGreaterThanOrEqual(95); expect(measured.longestGapMs).toBeLessThanOrEqual(500);
  expect(latencyP95Ms).not.toBeNull(); expect(latencyP95Ms!).toBeLessThanOrEqual(500);
  if (!videoOnly) {
  expect(measured.spectralPeakDb["997"]).toBeGreaterThan(-70);
  // This is a repeatable synthetic spectral exclusion check, not real Discord acceptance.
  expect(measured.spectralPeakDb["1801"]! - measured.spectralPeakDb["997"]!).toBeLessThan(-35);
  expect(measured.spectralPeakDb["2753"]! - measured.spectralPeakDb["997"]!).toBeLessThan(-35);
  }
  await page.close();
});
