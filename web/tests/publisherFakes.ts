import { vi } from "vitest";
import type { PublishReservation } from "../src/api/contracts";
export const grant: PublishReservation = { streamId: "22222222-2222-2222-2222-222222222222", path: "rooms/11111111111111111111111111111111/streams/22222222222222222222222222222222/main", whipUri: "/media/rooms/11111111111111111111111111111111/streams/22222222222222222222222222222222/main/whip", token: "test-media-token", expiresAt: "2099-01-01T00:00:00Z" };
export class Track extends EventTarget {
  readyState: MediaStreamTrackState = "live";
  constructor(public kind: string, public id = kind) { super(); }
  stop = vi.fn(() => { this.readyState = "ended"; });
  applyConstraints = vi.fn(async (_constraints: MediaTrackConstraints) => {});
  getSettings = () => ({ width: 1920, height: 1080, frameRate: 60 });
  end() { this.readyState = "ended"; this.dispatchEvent(new Event("ended")); }
}
export class Stream {
  constructor(public tracks: Track[] = [new Track("video")]) {}
  getTracks() { return this.tracks as unknown as MediaStreamTrack[]; }
  getVideoTracks() { return this.getTracks().filter(t => t.kind === "video"); }
  getAudioTracks() { return this.getTracks().filter(t => t.kind === "audio"); }
  removeTrack(track: MediaStreamTrack) { this.tracks = this.tracks.filter(t => t as unknown !== track); }
  asMedia() { return this as unknown as MediaStream; }
}
export const videoSdp = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\na=recvonly\r\na=candidate:1 1 UDP 1234 192.0.2.1 12345 typ host\r\n";
export const audioSdp = "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=recvonly\r\n";
export class Peer extends EventTarget {
  connectionState: RTCPeerConnectionState = "new";
  iceGatheringState: RTCIceGatheringState = "complete";
  localDescription = { type: "offer", sdp: videoSdp };
  transceivers: { direction: string; track: MediaStreamTrack; sender: { getParameters: () => RTCRtpSendParameters; setParameters: ReturnType<typeof vi.fn> }; setCodecPreferences: ReturnType<typeof vi.fn> }[] = [];
  addTransceiver(track: MediaStreamTrack, init: RTCRtpTransceiverInit) {
    const t = { direction: init.direction!, track, sender: { getParameters: () => ({ encodings: [{}] } as RTCRtpSendParameters), setParameters: vi.fn(async (_p: RTCRtpSendParameters) => {}) }, setCodecPreferences: vi.fn() }; this.transceivers.push(t); return t;
  }
  createOffer = vi.fn(async () => this.localDescription);
  setLocalDescription = vi.fn(async () => {});
  setRemoteDescription = vi.fn(async () => { this.connectionState = "connected"; this.dispatchEvent(new Event("connectionstatechange")); });
  close = vi.fn(() => { this.connectionState = "closed"; });
}
export function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject };
}
