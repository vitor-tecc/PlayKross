import { defineConfig } from "@playwright/test";
export default defineConfig({ testDir: "tests/protocol", outputDir: "../.artifacts/protocol/browser", timeout: 60000, workers: 1, reporter: "line", use: { channel: "msedge", headless: true, trace: "off", screenshot: "off", video: "off" } });
