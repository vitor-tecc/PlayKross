import { useCallback, useEffect, useLayoutEffect, useRef, useState, type HTMLAttributes } from "react";
import { flushSync } from "react-dom";

export const FULLSCREEN_IDLE_MS = 2000;

const interactiveSelector = "button, input, select, textarea, a[href], summary, [role='button'], [tabindex]:not([tabindex='-1'])";
const interactive = (target: EventTarget | null) => target instanceof Element ? target.closest(interactiveSelector) : null;

export function useFullscreenChrome(fullscreen: boolean, attentionRequired: boolean): {
  visible: boolean;
  surfaceProps: HTMLAttributes<HTMLDivElement>;
  overlayProps: HTMLAttributes<HTMLDivElement>;
} {
  const [visible, setVisible] = useState(!fullscreen || attentionRequired);
  const active = useRef(fullscreen);
  const attention = useRef(attentionRequired);
  const previousAttention = useRef(attentionRequired);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const hover = useRef(false), pressed = useRef(false), keyboardFocus = useRef(false), keyboard = useRef(false);
  const focusedControl = useRef<Element | null>(null);
  active.current = fullscreen; attention.current = attentionRequired;

  const clearIdle = useCallback(() => {
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
  }, []);
  const reveal = useCallback(() => {
    if (!active.current) return;
    clearIdle(); setVisible(true);
    if (!attention.current && !hover.current && !pressed.current && !keyboardFocus.current) {
      timer.current = setTimeout(() => { timer.current = null; setVisible(false); }, FULLSCREEN_IDLE_MS);
    }
  }, [clearIdle]);

  useLayoutEffect(() => {
    clearIdle();
    // A pointer click leaves the fullscreen button focused. Only new keyboard use holds chrome open.
    hover.current = false; pressed.current = false; keyboardFocus.current = false; keyboard.current = false;
    setVisible(!fullscreen || attention.current);
    if (!fullscreen) return clearIdle;
    const keydown = () => {
      keyboard.current = true;
      keyboardFocus.current = focusedControl.current !== null && focusedControl.current === document.activeElement;
      // Reveal before the browser performs Tab's default focus traversal.
      flushSync(reveal);
    };
    const release = () => { if (pressed.current) { pressed.current = false; reveal(); } };
    document.addEventListener("keydown", keydown, true);
    document.addEventListener("pointerup", release);
    document.addEventListener("pointercancel", release);
    return () => {
      clearIdle();
      document.removeEventListener("keydown", keydown, true);
      document.removeEventListener("pointerup", release);
      document.removeEventListener("pointercancel", release);
    };
  }, [fullscreen, clearIdle, reveal]);

  useEffect(() => {
    if (previousAttention.current !== attentionRequired) reveal();
    previousAttention.current = attentionRequired;
  }, [attentionRequired, reveal]);

  return {
    visible: !fullscreen || visible,
    surfaceProps: {
      onPointerMove: reveal,
      onPointerDownCapture: () => { keyboard.current = false; keyboardFocus.current = false; reveal(); },
      onTouchStart: reveal,
    },
    overlayProps: {
      onPointerOver: event => { if (interactive(event.target)) { hover.current = true; reveal(); } },
      onPointerOut: event => {
        if (hover.current && (!event.currentTarget.contains(event.relatedTarget as Node | null) || !interactive(event.relatedTarget))) {
          hover.current = false; reveal();
        }
      },
      onPointerDown: event => { if (interactive(event.target)) { pressed.current = true; reveal(); } },
      onFocus: event => { focusedControl.current = interactive(event.target); keyboardFocus.current = keyboard.current && focusedControl.current !== null; if (keyboardFocus.current) reveal(); },
      onBlur: event => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          focusedControl.current = null; keyboardFocus.current = false; reveal();
        }
      },
    },
  };
}
