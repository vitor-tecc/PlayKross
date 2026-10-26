import { test, expect } from "@playwright/test";

// Existing protocol scenarios assert English interface text.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("playkross-language", "en"));
});

test.use({ launchOptions: { args: ["--mute-audio"] } });

for (const scenario of [
  { name: "video only", requestAudio: false, availableAudio: false },
  { name: "requested audio unavailable", requestAudio: true, availableAudio: false },
  { name: "captured Opus audio", requestAudio: true, availableAudio: true },
]) {
  test(`Browser publication plays with ${scenario.name}`, async ({ browser }, info) => {
    test.skip(!process.env.JKILLER_BROWSER_PUBLISHING, "Requires Test-BrowserPublishing.ps1 and its owned local host.");
    const link = new URL(process.env.JKILLER_PROBE_JOIN_LINK!);
    const publisherContext = await browser.newContext(), viewerContext = await browser.newContext();
    try {
      await publisherContext.addInitScript(() => localStorage.setItem("playkross-language", "en"));
      await viewerContext.addInitScript(() => localStorage.setItem("playkross-language", "en"));
      const publisher = await publisherContext.newPage(), viewer = await viewerContext.newPage();
      // Replace only the source chooser with synthetic canvas/tone tracks. The
      // production controller, WHIP/WHEP, authentication and MediaMTX stay real.
      await publisher.addInitScript(({ availableAudio }) => {
        Object.defineProperty(navigator.mediaDevices, "getDisplayMedia", { value: async (options: DisplayMediaStreamOptions) => {
          const canvas = document.createElement("canvas"); canvas.width = 640; canvas.height = 360;
          const context = canvas.getContext("2d")!; let tick = 0;
          const timer = setInterval(() => { context.fillStyle = `hsl(${tick++ % 360},100%,50%)`; context.fillRect(0, 0, 640, 360); }, 33);
          const stream = canvas.captureStream(30);
          const video = stream.getVideoTracks()[0]!, stop = video.stop.bind(video);
          video.stop = () => { clearInterval(timer); stop(); };
          if (options.audio && availableAudio) {
            const audio = new AudioContext(), tone = audio.createOscillator(), destination = audio.createMediaStreamDestination();
            tone.connect(destination); tone.start(); void audio.resume();
            const track = destination.stream.getAudioTracks()[0]!, stopAudio = track.stop.bind(track);
            track.stop = () => { tone.stop(); void audio.close(); stopAudio(); };
            stream.addTrack(track);
          }
          return stream;
        } });
      }, scenario);
      await viewer.addInitScript(() => {
        const peers: RTCPeerConnection[] = [];
        (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers = peers;
        const Original = window.RTCPeerConnection;
        window.RTCPeerConnection = class extends Original {
          constructor(configuration?: RTCConfiguration) { super(configuration); peers.push(this); }
        };
      });
      for (const [page, nickname] of [[publisher, "Browser publisher"], [viewer, "Browser viewer"]] as const) {
        await page.goto(link.href);
        await page.getByLabel("Nickname", { exact: true }).fill(nickname);
        await page.getByRole("button", { name: "Join room", exact: true }).click();
        await expect(page.getByRole("button", { name: "Start sharing", exact: true })).toBeEnabled();
      }
      if (scenario.requestAudio) await publisher.getByLabel("Share captured audio", { exact: true }).check();
      const statuses: number[] = [];
      viewer.on("response", response => { if (response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/whep")) statuses.push(response.status()); });
      await publisher.getByRole("button", { name: "Start sharing", exact: true }).click();
      await expect(publisher.getByText(scenario.availableAudio ? "Sharing video and captured audio" : "Sharing video only", { exact: true })).toBeVisible({ timeout: 20000 });
      await viewer.getByRole("button", { name: "Watch stream", exact: true }).click();
      if (scenario.availableAudio) await viewer.getByRole("button", { name: "Enable sound", exact: true }).click();
      const video = viewer.locator("video");
      await expect(video).toHaveJSProperty("videoWidth", 640, { timeout: 20000 });
      await expect.poll(() => video.evaluate(v => (v as HTMLVideoElement).getVideoPlaybackQuality().totalVideoFrames)).toBeGreaterThan(10);
      const metrics = await viewer.evaluate(async () => {
        const peers = (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers;
        const stats = await peers.find(p => p.connectionState === "connected")!.getStats();
        const inbound = [...stats.values()].filter(s => s.type === "inbound-rtp");
        const video = inbound.find(s => s.kind === "video"), audio = inbound.find(s => s.kind === "audio");
        return { videoCodec: stats.get(video?.codecId)?.mimeType, decodedFrames: video?.framesDecoded,
          audioCodec: stats.get(audio?.codecId)?.mimeType ?? null, audioPackets: audio?.packetsReceived ?? 0, audioEnergy: audio?.totalAudioEnergy ?? 0 };
      });
      expect(metrics.videoCodec?.toLowerCase()).toBe("video/h264");
      expect(metrics.decodedFrames).toBeGreaterThan(0);
      expect(statuses).toContain(201); expect(statuses).not.toContain(400);
      if (scenario.availableAudio) {
        expect(metrics.audioCodec?.toLowerCase()).toBe("audio/opus");
        expect(metrics.audioPackets).toBeGreaterThan(0); expect(metrics.audioEnergy).toBeGreaterThan(0);
      } else expect(metrics.audioPackets).toBe(0);
      await publisher.getByRole("button", { name: "Stop sharing", exact: true }).click();
      await expect(viewer.locator(".stream-card")).toHaveCount(0);
      await expect.poll(() => viewer.evaluate(() => (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers.every(p => p.connectionState === "closed"))).toBe(true);
      await info.attach("browser-publication-evidence", { body: JSON.stringify({ scenario, browser: browser.version(), statuses, ...metrics }), contentType: "application/json" });
    } finally { await viewerContext.close(); await publisherContext.close(); }
  });
}
