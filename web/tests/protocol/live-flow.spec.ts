import { test, expect } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
test("native broadcaster controller sends the requested live smoke preset", async ({ page, browser }) => {
  test.skip(!process.env.JKILLER_LIVE_FLOW, "Run with the native live-flow harness.");
  const link = new URL(process.env.JKILLER_PROBE_JOIN_LINK ?? "");
  const height = Number(process.env.JKILLER_SMOKE_HEIGHT);
  await page.addInitScript(invitation => {
    sessionStorage.setItem("jk-invite", invitation);
    const peers: RTCPeerConnection[] = []; (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers = peers;
    const Original = window.RTCPeerConnection;
    window.RTCPeerConnection = class extends Original { constructor(configuration?: RTCConfiguration) { super(configuration); peers.push(this); } };
  }, decodeURIComponent(link.hash.slice(8)));
  await page.goto(link.origin);
  await page.getByLabel("Nickname", { exact: true }).fill("Live smoke viewer"); await page.getByRole("button", { name: "Join room" }).click();
  await page.getByRole("button", { name: "Watch stream" }).click({ timeout: 25000 });
  const video = page.locator("video");
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).videoHeight), { timeout: 30000 }).toBe(height);
  await page.getByRole("button", { name: "Enable sound" }).click();
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).getVideoPlaybackQuality().totalVideoFrames), { timeout: 10000 }).toBeGreaterThan(30);
  await page.waitForTimeout(3000);
  await expect.poll(async () => {
    const response = await page.request.get(new URL("/api/diagnostics", link.origin).href);
    const report = await response.json();
    return report.samples.some((s: { role: string; metrics: { height?: number; decodedFps?: number; presentedFps?: number } }) => s.role === "viewer" && s.metrics.height === height && s.metrics.decodedFps! > 0 && s.metrics.presentedFps! > 0);
  }, { timeout: 15000 }).toBe(true);
  const report = await (await page.request.get(new URL("/api/diagnostics", link.origin).href)).json();
  expect(report.samples.some((s: { role: string; metrics: { encodedFps?: number } }) => s.role === "publisher" && s.metrics.encodedFps! > 0)).toBe(true);
  const host = report.samples.find((s: { role: string }) => s.role === "host");
  expect(host.metrics.activeMediaSessions).toBeGreaterThanOrEqual(2); expect(host.metrics.mediaBytesSent).toBeGreaterThan(0);
  const reportText = JSON.stringify(report); expect(reportText).not.toContain("sessionToken"); expect(reportText).not.toContain("candidateAddress"); expect(reportText).not.toContain("imagePath");
  await test.info().attach(`redacted-${height}-report`, { body: Buffer.from(JSON.stringify(report, null, 2)), contentType: "application/json" });
  mkdirSync("../.artifacts/diagnostics", { recursive: true });
  writeFileSync(`../.artifacts/diagnostics/live-room-${height}.json`, JSON.stringify(report, null, 2));
  const metrics = await page.evaluate(async () => {
    const peer = (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers.find(value => value.connectionState === "connected");
    if (!peer) throw new Error("Actual receiver not connected.");
    const stats = await peer.getStats(); const inbound = [...stats.values()].filter(value => value.type === "inbound-rtp");
    const video = inbound.find(value => value.kind === "video"), audio = inbound.find(value => value.kind === "audio");
    const canvas = document.createElement("canvas"); canvas.width = 32; canvas.height = 32;
    const context = canvas.getContext("2d")!; context.drawImage(document.querySelector("video")!, 0, 0, 32, 32);
    const pixels = context.getImageData(0, 0, 32, 32).data;
    let sum = 0, squares = 0;
    for (let index = 0; index < pixels.length; index += 4) { const luminance = (pixels[index]! + pixels[index+1]! + pixels[index+2]!) / 3; sum += luminance; squares += luminance*luminance; }
    const pixelLumaVariance = squares / 1024 - (sum / 1024) ** 2;
    return { codec: stats.get(video.codecId)?.mimeType, width: video.frameWidth, height: video.frameHeight, decoded: video.framesDecoded,
      reportedDecodeFps: video.framesPerSecond, pixelLumaVariance, audioCodec: stats.get(audio.codecId)?.mimeType, audioPackets: audio.packetsReceived, audioEnergy: audio.totalAudioEnergy };
  });
  expect(metrics.codec.toLowerCase()).toBe("video/h264"); expect(metrics.audioCodec.toLowerCase()).toBe("audio/opus"); expect(metrics.audioPackets).toBeGreaterThan(0);
  expect(metrics.pixelLumaVariance, "Actual received video must contain varied pixels, rather than a blank decoded frame").toBeGreaterThan(1);
  console.log(JSON.stringify({ browser: browser.version(), topology: link.protocol === "https:" ? "same-PC viewer via HTTPS origin; not external-network evidence" : "loopback", check: "short-live-smoke-only", captureMode: process.env.JKILLER_PROBE_CAPTURE_MODE ?? "application", audioValidation: process.env.JKILLER_PROBE_CAPTURE_MODE === "video-only" ? "pending; application audio disabled" : "short delivery only; Discord and sync pending", ...metrics }));
  await page.getByRole("button", { name: "Fullscreen", exact: true }).click();
  await video.hover();
  await page.getByRole("button", { name: "Return to room" }).click();
  await page.close();
});
