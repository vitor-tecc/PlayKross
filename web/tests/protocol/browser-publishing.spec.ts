import { test, expect } from "@playwright/test";

test.use({ launchOptions: { args: ["--mute-audio"] } });

for (const scenario of [
  { name: "video only", requestAudio: false, availableAudio: false },
  { name: "requested audio unavailable", requestAudio: true, availableAudio: false },
  { name: "captured Opus audio", requestAudio: true, availableAudio: true },
  { name: "1080p60 video", requestAudio: false, availableAudio: false, width: 1920, height: 1080, frameRate: 60 },
].map(scenario => ({ width: 640, height: 360, frameRate: 30, ...scenario }))) {
  test(`Browser publication plays with ${scenario.name}`, async ({ browser }, info) => {
    test.skip(!process.env.JKILLER_BROWSER_PUBLISHING, "Requires Test-BrowserPublishing.ps1 and its owned local host.");
    const link = new URL(process.env.JKILLER_PROBE_JOIN_LINK!);
    const publisherContext = await browser.newContext(), viewerContext = await browser.newContext();
    try {
      const publisher = await publisherContext.newPage(), viewer = await viewerContext.newPage();
      // Replace only the source chooser with synthetic canvas/tone tracks. The
      // production controller, WHIP/WHEP, authentication and MediaMTX stay real.
      await publisher.addInitScript(({ availableAudio, width, height, frameRate }) => {
        Object.defineProperty(navigator.mediaDevices, "getDisplayMedia", { value: async (options: DisplayMediaStreamOptions) => {
          const canvas = document.createElement("canvas"); canvas.width = width; canvas.height = height;
          const context = canvas.getContext("2d")!; let tick = 0;
          let animation = 0;
          const draw = () => {
            context.fillStyle = "#15263f"; context.fillRect(0, 0, width, height);
            context.fillStyle = "#f0b34c"; context.fillRect((tick++ * 8) % width, 0, 64, height);
            animation = requestAnimationFrame(draw);
          };
          draw();
          const stream = canvas.captureStream(frameRate);
          const video = stream.getVideoTracks()[0]!, stop = video.stop.bind(video);
          video.stop = () => { cancelAnimationFrame(animation); stop(); };
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
      for (const page of [publisher, viewer]) await page.addInitScript(() => {
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
      await expect(video).toHaveJSProperty("videoWidth", scenario.width, { timeout: 20000 });
      await expect(video).toHaveJSProperty("videoHeight", scenario.height);
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
      let highFrameRateEvidence = null;
      if (scenario.frameRate === 60) {
        // Let startup settle, then measure actual decode progress; configuration
        // alone would miss a sender still capped at 30 fps.
        await viewer.waitForTimeout(5000);
        const decoded = await viewer.evaluate(async () => {
          const peer = (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers.find(p => p.connectionState === "connected")!;
          const sample = async () => {
            const stats = await peer.getStats();
            const inbound = [...stats.values()].find(s => s.type === "inbound-rtp" && s.kind === "video")!;
            return { at: performance.now(), id: inbound.id, frames: inbound.framesDecoded as number, width: inbound.frameWidth as number, height: inbound.frameHeight as number };
          };
          const before = await sample(); await new Promise(resolve => setTimeout(resolve, 5000)); const after = await sample();
          if (before.id !== after.id) throw new Error("Receiver changed during FPS measurement.");
          return { width: after.width, height: after.height, seconds: (after.at - before.at) / 1000, fps: (after.frames - before.frames) * 1000 / (after.at - before.at) };
        });
        const sender = await publisher.evaluate(async () => {
          const peer = (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers.find(p => p.connectionState === "connected")!;
          const sender = peer.getSenders().find(s => s.track?.kind === "video")!;
          const stats = await sender.getStats();
          const outbound = [...stats.values()].find(s => s.type === "outbound-rtp" && s.kind === "video");
          const { width, height, frameRate } = sender.track!.getSettings();
          return { encodings: sender.getParameters().encodings, capture: { width, height, frameRate }, fps: outbound?.framesPerSecond,
            targetBitrate: outbound?.targetBitrate, qualityLimitationReason: outbound?.qualityLimitationReason };
        });
        highFrameRateEvidence = { decoded, sender };
        await info.attach("1080p60-evidence", { body: JSON.stringify(highFrameRateEvidence), contentType: "application/json" });
        console.log(JSON.stringify(highFrameRateEvidence));
        expect(decoded.width).toBe(1920); expect(decoded.height).toBe(1080); expect(decoded.fps).toBeGreaterThanOrEqual(55);
        expect(sender.encodings).toHaveLength(1); expect(sender.encodings[0]).toMatchObject({ maxBitrate: 12_000_000, maxFramerate: 60 });
      }
      await publisher.getByRole("button", { name: "Stop sharing", exact: true }).click();
      await expect(viewer.locator(".stream-card")).toHaveCount(0);
      await expect.poll(() => viewer.evaluate(() => (window as unknown as { probePeers: RTCPeerConnection[] }).probePeers.every(p => p.connectionState === "closed"))).toBe(true);
      await info.attach("browser-publication-evidence", { body: JSON.stringify({ scenario, browser: browser.version(), statuses, ...metrics, highFrameRateEvidence }), contentType: "application/json" });
    } finally { await viewerContext.close(); await publisherContext.close(); }
  });
}
