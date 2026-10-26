import { test, expect, type Locator } from "@playwright/test";

// Existing protocol scenarios assert English interface text.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("playkross-language", "en"));
});
import { writeFile } from "node:fs/promises";
import { join } from "node:path";
import { checkFullscreen } from "./fullscreen";

test("TwoPublishersRemainIndependent", async ({ page, browser }, info) => {
  test.skip(!process.env.JKILLER_CONCURRENT_RUN, "Requires the prepared portable concurrent harness.");
  test.setTimeout(120000);
  const directory=process.env.JKILLER_CONCURRENT_RUN!;
  const link=new URL(process.env.JKILLER_PROBE_JOIN_LINK!);
  await page.addInitScript(invite => {
    sessionStorage.setItem("jk-invite",invite);
    const peers:RTCPeerConnection[]=[]; (window as any).jkTestPeers=peers;
    const Original=window.RTCPeerConnection;
    window.RTCPeerConnection=class extends Original {constructor(configuration?:RTCConfiguration){super(configuration);peers.push(this);}};
  },decodeURIComponent(link.hash.slice(8)));
  await page.goto(link.origin);
  await page.getByLabel("Nickname",{exact:true}).fill("Concurrent functional viewer");
  await page.getByRole("button",{name:"Join room"}).click();
  const card=(name:string)=>page.locator(".stream-card").filter({has:page.locator("strong",{hasText:name})});
  const a=card("Concurrent A"), b=card("Concurrent B");
  await expect(a).toHaveCount(1,{timeout:25000});await expect(b).toHaveCount(1);
  const aId=await a.getAttribute("data-stream-id"), bId=await b.getAttribute("data-stream-id");expect(aId).not.toBe(bId);
  for(const selected of [a,b]){await selected.getByRole("button",{name:"Watch stream",exact:true}).click();await selected.getByRole("button",{name:"Enable sound",exact:true}).click();}
  async function counters(selected:Locator) {
    return selected.evaluate(async element=>{
      const video=element.querySelector("video")!; const stream=video.srcObject as MediaStream|null;
      const peer=((window as any).jkTestPeers as RTCPeerConnection[]).find(p=>p.connectionState === "connected" && p.getReceivers().some(r=>stream?.getTracks().includes(r.track)));
      if(!peer)return {frames:0,audio:0,energy:0};
      const all=await peer.getStats(); const audio=[...all.values()].find(s=>s.type === "inbound-rtp" && s.kind === "audio");
      return {frames:video.getVideoPlaybackQuality().totalVideoFrames,audio:audio?.packetsReceived??0,energy:audio?.totalAudioEnergy??0};
    });
  }
  for(const selected of [a,b]){await expect.poll(async()=> (await counters(selected)).frames,{timeout:30000}).toBeGreaterThan(30);await expect.poll(async()=> (await counters(selected)).audio).toBeGreaterThan(0);}
  // Save the actual B element/media/peer; stop/restart A must not recreate B.
  await b.evaluate(e=>{const v=e.querySelector("video")!;(window as any).jkIndependentB={element:e,video:v,media:v.srcObject,peers:(window as any).jkTestPeers.filter((p:RTCPeerConnection)=>p.connectionState === "connected" && p.getReceivers().some(r=>(v.srcObject as MediaStream).getTracks().includes(r.track)))};});
  const before=await counters(b);
  await writeFile(join(directory,"A","stop-publication"),"stop");
  await expect(a).toHaveCount(0,{timeout:20000});
  await expect.poll(async()=> (await counters(b)).frames).toBeGreaterThan(before.frames+20);
  await writeFile(join(directory,"A","restart-publication"),"restart");
  await expect(a).toHaveCount(1,{timeout:30000});expect(await a.getAttribute("data-stream-id")).not.toBe(bId);
  await a.getByRole("button",{name:"Watch stream",exact:true}).click();
  await expect.poll(async()=> (await counters(a)).frames,{timeout:30000}).toBeGreaterThan(30);
  expect(await b.evaluate(e=>{const prior=(window as any).jkIndependentB;return e===prior.element&&e.querySelector("video")===prior.video&&prior.video.srcObject===prior.media&&prior.peers.every((p:RTCPeerConnection)=>p.connectionState==="connected");})).toBe(true);
  await expect.poll(async()=> (await counters(b)).audio).toBeGreaterThan(before.audio);
  await b.getByRole("button",{name:"Mute",exact:true}).click();await expect(b.locator("video")).toHaveJSProperty("muted",true);
  await b.getByRole("button",{name:"Enable sound",exact:true}).click();await expect(b.locator("video")).toHaveJSProperty("muted",false);
  const evidence={browser:browser.version(),topology:"same-PC loopback; two portable helpers",captureMode:process.env.JKILLER_PROBE_CAPTURE_MODE,physicalBrowserAudio:process.env.JKILLER_PROBE_CAPTURE_MODE==="system-output"?"disabled: explicit --mute-audio":"not evaluated for audibility",streamIds:[aId,bId],a:await counters(a),b:await counters(b),humanListening:"pending",syntheticToneDetection:"pending",maximumCapacity:"unproven"};
  await writeFile(join(directory,"browser-transport-before-fullscreen.json"),JSON.stringify({...evidence,scope:"Transport/isolation/sound-toggle checks reached; fullscreen and whole-test outcome are separate"},null,2));
  await checkFullscreen(page,b);
  await info.attach("concurrent-transport-evidence",{body:JSON.stringify(evidence,null,2),contentType:"application/json"});
  await writeFile(join(directory,"browser-evidence.json"),JSON.stringify(evidence,null,2));
});
