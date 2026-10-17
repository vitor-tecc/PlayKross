import { useEffect, useState } from "react";

declare global { interface Window { playkrossPlaybackAudioBlocked?: boolean } }

const eventName = "playkross-playback-audio-blocked";
export function useHostPlaybackAudioBlocked(): boolean {
  const [blocked, setBlocked] = useState(() => window.playkrossPlaybackAudioBlocked === true);
  useEffect(() => {
    const changed = (event: Event) => {
      const value: unknown = (event as CustomEvent<unknown>).detail;
      if (typeof value === "boolean") setBlocked(value);
    };
    window.addEventListener(eventName, changed);
    // Cover an update between render and subscription, including newly opened cards.
    setBlocked(window.playkrossPlaybackAudioBlocked === true);
    return () => window.removeEventListener(eventName, changed);
  }, []);
  return blocked;
}
