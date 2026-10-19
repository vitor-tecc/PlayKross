# M3 video-only browser playback regression

2026-10-18, branch `codex/m3-viewing-refinements`, follow-up to `137fd9a`. This records the narrow fix committed with this report. Earlier [browser verification](2026-10-18-m3-browser.md), M2 acceptance and native AMD evidence retain their revision/package limits.

## Failure and cause

The owner's Firefox 158 report showed repeated WHEP OPTIONS 204 / POST 400, complete candidate gathering, ICE/connection still `new`, and about 60 seconds left on the playback grant. This is a signaling rejection before ICE connectivity checks.

A real Edge browser sender using a synthetic canvas video track reproduced POST 400 with MediaMTX 1.21.2's `codecs not supported by client` response. Adding only an Opus audio track made playback connect. The pinned [MediaMTX peer implementation](https://github.com/bluenviron/mediamtx/blob/v1.21.2/internal/protocols/webrtc/peer_connection.go) adds a PCMU placeholder sender when the publication has no audio. PlayKross restricted its receive audio offer to Opus, leaving that placeholder without a matching codec and causing the whole WHEP negotiation to fail.

The viewer now permits Opus and PCMU in its receive-only audio offer. Browser publications still send H.264 and optional Opus, with one encoding per captured track. No microphone, synthetic publisher audio, transcoding, extra broadcaster encoding or CPU fallback is added to the product.

## Verification

- Test-first unit regression failed with PCMU missing from the receive offer, then passed after the change. The real browser-to-browser regression also failed on the old production build: the publisher was live but the viewer's video width remained zero after repeated rejected negotiations.
- `./scripts/Verify.ps1 -Mode CodeOnly`: passed **292 managed tests, 98 browser tests and 43 native checks**, plus production/app/probe builds and setup/headless checks. Native plugin compiled, not installed. An initial sandboxed run failed in Visual Studio file tracking with access denied; the complete rerun outside the sandbox passed. Logs: ignored `.artifacts/m3-video-only-codeonly*.log`.
- `./scripts/Test-BrowserPublishing.ps1`: passed **3 real local protocol cases** with Edge **155.0.4283.62** and pinned MediaMTX **1.21.2**: video-only, requested-but-unavailable audio, and available Opus audio. Each case used separate publisher/viewer browser contexts, the built production website, authenticated reservation/WHIP/WHEP and actual encoded/decoded media. Each required decoded H.264 video, POST 201 without POST 400, and stream/receiver removal after Stop sharing. The Opus case also required received packets and positive audio energy; the two silent cases received no audio RTP packets. Physical test sound was muted by the browser launch option.
- Installed **Firefox 158.0**, in a disposable default profile, received the Edge browser's video-only synthetic feed through the built website. Same-PC Ethernet media, localhost signaling: first attempt connected, 640x360 H.264, 41 decoded frames and 35 additional decoded frames over approximately 1.2 seconds; remote answer included `PCMU/8000`. No existing Firefox profile was used. An initial sandboxed driver could not launch tab subprocesses; the successful check used an unsandboxed owned driver. Numeric result: ignored `.artifacts/m3-video-only-firefox.json`.
- A fresh read-only GPT-6.1 Sol review at high reasoning found no actionable issues in the production fix, unit/protocol regressions or owned test-service runner.

The repeatable runner builds the website/host, starts its own local services, restores environment variables and stops only those services on exit. It refuses to reuse occupied service ports. Use `-MediaInterface Ethernet` (or the actual exact interface alias) when appropriate for the local routing setup. The automated source replaces only the chooser with canvas/tone tracks; all publication and playback transport remains real. Raw logs and private invitations stay ignored.

## Limits

This is short real transport/decode evidence using synthetic sources, not acceptance of the actual display chooser, screen/window/tab capture, human-audible captured audio, external-network stability, native/browser coexistence, WebView media delivery, AMD hardware operation or game impact. Those M3 checks remain pending. Firefox presented FPS was not measured; inbound decode progress is not a presentation benchmark. No release binary or portable package was created.
