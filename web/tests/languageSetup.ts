import { beforeEach } from "vitest";
import { setLanguage } from "../src/i18n";

// Existing behavior tests assert the English copy explicitly.
beforeEach(() => setLanguage("en"));
// Remembered browser-broadcast quality must not leak between tests.
beforeEach(() => localStorage.removeItem("playkross-broadcast-quality"));
