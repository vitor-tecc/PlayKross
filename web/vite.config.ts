import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
export default defineConfig({ publicDir: "../assets/branding", plugins: [react()], test: { environment: "jsdom", include: ["tests/**/*.test.ts", "tests/**/*.test.tsx"] }, server: { proxy: { "/api": "http://localhost:5000", "/hubs": { target: "http://localhost:5000", ws: true }, "/media": "http://localhost:5000", "/media-resources": "http://localhost:5000" } } });
