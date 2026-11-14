import { t, useLanguage } from "../i18n";
import { useState } from "react";
import { downloadBlob } from "./download";
import type { ViewerDiagnostics } from "../media/playerDiagnostics";
export function DiagnosticsPanel({ sample }: { sample: ViewerDiagnostics | null }) {
  useLanguage();
  const [exportFailed, setExportFailed] = useState(false);
  const exportRoom = async () => {
    setExportFailed(false);
    try {
      const response = await fetch("/api/diagnostics", { credentials: "same-origin" });
      if (!response.ok) throw new Error();
      downloadBlob(await response.blob(), "jkiller-diagnostics.json");
    } catch { setExportFailed(true); }
  };
  const value = (v: number | null | undefined, unit: string) => v === null || v === undefined ? t("unavailable") : `${v.toFixed(1)} ${unit}`;
  return <details className="diagnostics"><summary title={t("Playback diagnostics")}><span aria-hidden="true">ⓘ</span><span className="sr-only">{t("Playback diagnostics")}</span></summary>
    <div className="diagnostics-content"><dl><dt>{t("Delivered video")}</dt><dd>{sample?.width && sample.height ? `${sample.width} × ${sample.height}` : t("unavailable")} · {sample?.codec ?? t("codec unavailable")}</dd>
      <dt>{t("Received video")}</dt><dd>{value(sample?.receivedMbps, "Mbps")}</dd>
      <dt>{t("Decoded / presented")}</dt><dd>{value(sample?.decodedFps, "FPS")} / {value(sample?.presentedFps, "FPS")}</dd>
      <dt>{t("Packet loss / jitter")}</dt><dd>{value(sample?.packetLossPercent, "%")} / {value(sample?.jitterMs, "ms")}</dd>
      <dt>{t("ICE round trip")}</dt><dd>{value(sample?.iceRttMs, "ms")} · {t(sample?.connectionPath ?? t("unknown"))}</dd>
      <dt>{t("First frame / stalls over 500 ms")}</dt><dd>{value(sample?.startupMs, "ms")} / {sample?.stalls ?? t("unavailable")}</dd>
    </dl><p>{t("Five-second rolling measurements. Presented frames were submitted for composition; physical display timing and end-to-end video latency are unavailable. ICE round trip measures the network. Capture FPS is measured in the broadcaster.")}</p>
    <button onClick={() => { void exportRoom(); }}>{t("Export room diagnostics")}</button>
    {exportFailed && <p role="alert">{t("Diagnostic export is unavailable. Rejoin the room and retry.")}</p>}
  </div></details>;
}
