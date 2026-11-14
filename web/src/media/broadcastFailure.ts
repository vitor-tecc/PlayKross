/** A publication failure whose message tells the broadcaster what to do. */
export class BroadcastFailure extends Error {
  constructor(public readonly reason: "codec" | "connection" | "other", message: string) { super(message); this.name = "BroadcastFailure"; }
}
export const codecFailureMessage = "This browser cannot send H.264 video. In Firefox, enable the OpenH264 plugin (Add-ons and themes → Plugins), or use Chrome or Edge.";
export const connectionFailureMessage = "The broadcast media connection failed. Check UDP/firewall/relay settings. Firefox on the room host PC needs the server started with -MediaInterface (see the setup guide).";
