import { test, expect } from "@playwright/test";
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
  const metrics = await page.evaluate(async () => {
    const peer = (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers.find(value => value.connectionState === "connected");
    if (!peer) throw new Error("Actual receiver not connected.");
    const stats = await peer.getStats(); const inbound = [...stats.values()].filter(value => value.type === "inbound-rtp");
    const video = inbound.find(value => value.kind === "video"), audio = inbound.find(value => value.kind === "audio");
    return { codec: stats.get(video.codecId)?.mimeType, width: video.frameWidth, height: video.frameHeight, decoded: video.framesDecoded,
      reportedDecodeFps: video.framesPerSecond, audioCodec: stats.get(audio.codecId)?.mimeType, audioPackets: audio.packetsReceived, audioEnergy: audio.totalAudioEnergy };
  });
  expect(metrics.codec.toLowerCase()).toBe("video/h264"); expect(metrics.audioCodec.toLowerCase()).toBe("audio/opus"); expect(metrics.audioPackets).toBeGreaterThan(0);
  console.log(JSON.stringify({ browser: browser.version(), topology: "loopback", check: "short-live-smoke-only", ...metrics }));
  await page.getByRole("button", { name: "Fullscreen", exact: true }).click(); await page.getByRole("button", { name: "Return to room" }).click();
  await page.close();
});
