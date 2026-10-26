import { test, expect } from "@playwright/test";

// Existing protocol scenarios assert English interface text.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("playkross-language", "en"));
});
import { writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { checkFullscreen } from "./fullscreen";
test("real native H264 and process-only Opus reach one Edge viewer", async ({ page, browser }, info) => {
  const link = new URL(process.env.JKILLER_PROBE_JOIN_LINK ?? "");
  const invite = decodeURIComponent(link.hash.slice("#invite=".length));
  await page.addInitScript(invitation => {
    sessionStorage.setItem("jk-invite", invitation);
    const peers: RTCPeerConnection[] = []; (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers = peers;
    const Original = window.RTCPeerConnection;
    window.RTCPeerConnection = class extends Original { constructor(configuration?: RTCConfiguration) { super(configuration); peers.push(this); } };
  }, invite);
  await page.goto(link.origin);
  await page.getByLabel("Nickname", { exact: true }).fill("Protocol viewer"); await page.getByRole("button", { name: "Join room" }).click();
  await page.getByRole("button", { name: "Watch stream" }).click({ timeout: 25000 });
  const video = page.locator("video");
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).videoWidth), { timeout: 30000 }).toBe(1280);
  await page.getByRole("button", { name: "Enable sound" }).click();
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).getVideoPlaybackQuality().totalVideoFrames), { timeout: 10000 }).toBeGreaterThan(30);
  const metrics = await page.evaluate(async () => {
    const peers = (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers;
    const peer = peers.filter(value => value.connectionState === "connected").at(-1); if (!peer) throw new Error("No connected actual WebRTC receiver.");
    const all = await peer.getStats();
    const inbound = [...all.values()].filter(value => value.type === "inbound-rtp");
    const video = inbound.find(value => value.kind === "video"); const audio = inbound.find(value => value.kind === "audio");
    return { topology: "local-loopback", evidence: "real-synthetic-native-media", connection: peer.connectionState,
      videoCodec: video?.codecId ? all.get(video.codecId)?.mimeType : null, audioCodec: audio?.codecId ? all.get(audio.codecId)?.mimeType : null,
      width: video?.frameWidth, height: video?.frameHeight, decodedFrames: video?.framesDecoded, videoPackets: video?.packetsReceived,
      audioPackets: audio?.packetsReceived, audioEnergy: audio?.totalAudioEnergy,
      presentedTotal: document.querySelector("video")?.getVideoPlaybackQuality().totalVideoFrames };
  });
  expect(metrics.videoCodec?.toLowerCase()).toBe("video/h264"); expect(metrics.audioCodec?.toLowerCase()).toBe("audio/opus");
  expect(metrics.audioPackets).toBeGreaterThan(0); expect(metrics.audioEnergy).toBeGreaterThan(0);
  await info.attach("protocol-evidence", { body: JSON.stringify({ browser: browser.version(), ...metrics }, null, 2), contentType: "application/json" });
  console.log(JSON.stringify({ browser: browser.version(), ...metrics }));
  await checkFullscreen(page, page.locator(".stream-card"));
  await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).videoWidth)).toBe(1280);
  await writeFile(resolve("../.artifacts/protocol/revoke-publication"), "revoke");
  await expect(video).toHaveCount(0, { timeout: 10000 });
  await expect.poll(() => page.evaluate(() => (window as unknown as { jkTestPeers: RTCPeerConnection[] }).jkTestPeers.every(peer => peer.connectionState === "closed")), { timeout: 10000 }).toBe(true);
  console.log("PASS: real fullscreen return preserves playback; ownership revocation removes the player and closes its receiver.");
});
