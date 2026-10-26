import { beforeEach } from "vitest";
import { setLanguage } from "../src/i18n";

// Existing behavior tests assert the English copy explicitly.
beforeEach(() => setLanguage("en"));
