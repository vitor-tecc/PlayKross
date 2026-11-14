export interface BroadcastQuality { width: number; height: number; fps: number; bitrateMbps: number }

// Match the native presets; these are requested limits, not measured output.
export function qualityPreset(height: number, fps: number): BroadcastQuality {
  const width = height === 480 ? 854 : height === 720 ? 1280 : 1920;
  const bitrateMbps = height === 480 ? (fps === 30 ? 2 : 3) : height === 720 ? (fps === 30 ? 4 : 6) : (fps === 30 ? 8 : 15);
  const quality = { width, height, fps, bitrateMbps };
  if (!validQuality(quality)) throw new Error("Unsupported broadcast preset");
  return quality;
}
export const defaultQuality: Readonly<BroadcastQuality> = Object.freeze(qualityPreset(720, 30));
export function validQuality(q: BroadcastQuality): boolean {
  return ((q.width === 854 && q.height === 480) || (q.width === 1280 && q.height === 720) || (q.width === 1920 && q.height === 1080)) &&
    (q.fps === 30 || q.fps === 60) && Number.isFinite(q.bitrateMbps) && q.bitrateMbps >= 1 && q.bitrateMbps <= 25 && q.bitrateMbps % 0.5 === 0;
}

// Per-browser convenience only: the last started quality. Captured audio is
// deliberately never remembered; it stays opt-in for every broadcast.
const preferenceKey = "playkross-broadcast-quality";
export function loadQualityPreference(storage: Pick<Storage, "getItem"> | undefined = globalThis.localStorage): BroadcastQuality {
  try {
    const saved: unknown = JSON.parse(storage?.getItem(preferenceKey) ?? "null");
    if (saved && typeof saved === "object") {
      const { width, height, fps, bitrateMbps } = saved as Record<string, unknown>;
      const quality = { width, height, fps, bitrateMbps } as BroadcastQuality;
      if (validQuality(quality)) return quality;
    }
  } catch { /* Storage may be disabled or hold an older format. */ }
  return { ...defaultQuality };
}
export function saveQualityPreference(quality: BroadcastQuality, storage: Pick<Storage, "setItem"> | undefined = globalThis.localStorage): void {
  if (!validQuality(quality)) return;
  try { storage?.setItem(preferenceKey, JSON.stringify(quality)); } catch { /* Storage may be disabled. */ }
}
