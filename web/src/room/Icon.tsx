import type { ReactNode } from "react";

export type IconName = "eye" | "stop" | "play" | "sound" | "mute" | "fullscreen" | "focus" | "grid" | "retry";

// Static outline glyphs (24×24, stroke-based) shared by all stream cards.
const paths: Record<IconName, ReactNode> = {
  eye: <><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z" /><circle cx="12" cy="12" r="3" /></>,
  stop: <rect x="6" y="6" width="12" height="12" rx="1" />,
  play: <path d="m8 5 11 7-11 7V5Z" />,
  sound: <><path d="M11 4 5 9H2v6h3l6 5V4Z" /><path d="M15 8a6 6 0 0 1 0 8m3-11a10 10 0 0 1 0 14" /></>,
  mute: <><path d="M11 4 5 9H2v6h3l6 5V4Z" /><path d="m16 9 6 6m0-6-6 6" /></>,
  fullscreen: <path d="M9 3H3v6m12-6h6v6M3 15v6h6m6 0h6v-6" />,
  focus: <><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M16 4v16M16 12h5" /></>,
  grid: <><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><rect x="14" y="14" width="7" height="7" rx="1" /></>,
  retry: <><path d="M20 7v5h-5M4 17v-5h5" /><path d="M6 7a7 7 0 0 1 12-1l2 6M4 12l2 6a7 7 0 0 0 12-1" /></>,
};

export function Icon({ name }: { name: IconName }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}
