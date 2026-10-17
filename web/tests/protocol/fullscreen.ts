import { expect, type Locator, type Page } from "@playwright/test";

export async function checkFullscreen(page: Page, card: Locator) {
  const before = await card.evaluate(element => {
    const video = element.querySelector("video")!;
    (window as any).jkFullscreenIdentity = { video, media: video.srcObject, peers: (window as any).jkTestPeers.filter((p: RTCPeerConnection) => p.connectionState === "connected") };
    return video.getVideoPlaybackQuality().totalVideoFrames;
  });
  await card.getByRole("button", { name: "Fullscreen", exact: true }).click();
  await expect.poll(() => card.evaluate(e => document.fullscreenElement === e)).toBe(true);
  await expect(card).toHaveClass(/fullscreen-chrome-hidden/);
  const geometry = await card.evaluate(e => ({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height,viewportWidth:innerWidth,viewportHeight:innerHeight,cursor:getComputedStyle(e).cursor,overlays:[...e.querySelectorAll(".stream-overlay")].map(o=>getComputedStyle(o).opacity)}));
  expect(geometry.width).toBeCloseTo(geometry.viewportWidth, 0); expect(geometry.height).toBeCloseTo(geometry.viewportHeight, 0);
  expect(geometry.cursor).toBe("none"); expect(geometry.overlays).toEqual(["0", "0"]);
  const box = (await card.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await expect(card).not.toHaveClass(/fullscreen-chrome-hidden/);
  await expect(card).toHaveClass(/fullscreen-chrome-hidden/, {timeout:4000});
  await page.keyboard.press("Tab");
  await expect(card).not.toHaveClass(/fullscreen-chrome-hidden/);
  await page.keyboard.press("Escape");
  await expect.poll(() => page.evaluate(() => document.fullscreenElement === null)).toBe(true);
  await expect(card).not.toHaveClass(/fullscreen-chrome-hidden/);
  await expect.poll(() => card.locator("video").evaluate(v => (v as HTMLVideoElement).getVideoPlaybackQuality().totalVideoFrames)).toBeGreaterThan(before);
  expect(await card.evaluate(e => {
    const prior=(window as any).jkFullscreenIdentity;
    return e.querySelector("video")===prior.video && prior.video.srcObject===prior.media && prior.peers.every((p:RTCPeerConnection)=>p.connectionState === "connected" && (window as any).jkTestPeers.includes(p));
  })).toBe(true);
}
