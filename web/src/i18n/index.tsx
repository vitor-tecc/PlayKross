import { useSyncExternalStore } from "react";
import messages from "./pt-BR.json";

export type Language = "pt-BR" | "en";
const translations: Record<string, string> = messages;
const templates = Object.entries(translations).filter(([key]) => key.includes("{0}")).map(([key, value]) => {
  const indexes: number[] = [];
  const pattern = key.split(/(\{\d+\})/).map(part => {
    if (/^\{\d+\}$/.test(part)) { indexes.push(Number(part.slice(1, -1))); return "(.*?)"; }
    return part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }).join("");
  return { pattern: new RegExp("^" + pattern + "$", "s"), value, indexes };
});
const listeners = new Set<() => void>();
function readLanguage(): Language {
  try { return localStorage.getItem("playkross-language") === "en" ? "en" : "pt-BR"; }
  catch { return "pt-BR"; }
}
let language = readLanguage();
function updateDocument() {
  document.documentElement.lang = language;
  document.title = t("PlayKross · Room");
}
export function t(text: string, ...values: (string | number)[]): string {
  if (language === "pt-BR" && !translations[text] && values.length === 0) {
    const reconnect = /^Reconnecting \((\d+)\/3\): (.*)$/s.exec(text);
    if (reconnect) return t("Reconnecting ({0}/3): {1}", reconnect[1]!, t(reconnect[2]!));
    const suffix = " Automatic retries stopped; press Retry when ready.";
    if (text.endsWith(suffix)) return t(text.slice(0, -suffix.length)) + t(suffix);
    for (const template of templates) {
      const match = template.pattern.exec(text);
      if (match) return template.value.replace(/\{(\d+)\}/g, (_, index: string) => match[template.indexes.indexOf(Number(index)) + 1] ?? "");
    }
  }
  const translated = language === "pt-BR" ? translations[text] ?? text : text;
  return translated.replace(/\{(\d+)\}/g, (match, index: string) => String(values[Number(index)] ?? match));
}
export function setLanguage(value: Language) {
  language = value;
  try { localStorage.setItem("playkross-language", value); } catch { /* Storage may be disabled. */ }
  updateDocument();
  listeners.forEach(listener => listener());
}
export function useLanguage() {
  return useSyncExternalStore(listener => { listeners.add(listener); return () => { listeners.delete(listener); }; }, () => language);
}
window.addEventListener("storage", event => {
  if (event.key === "playkross-language" || event.key === null) {
    language = readLanguage(); updateDocument(); listeners.forEach(listener => listener());
  }
});
updateDocument();
export function LanguageSelector() {
  const selected = useLanguage();
  return <label className="language-selector"><span className="sr-only">{t("Language")}</span>
    <select value={selected} onChange={event => setLanguage(event.target.value as Language)}>
      <option value="pt-BR">Português (BR)</option><option value="en">English</option>
    </select>
  </label>;
}
