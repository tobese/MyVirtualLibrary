import { defineConfig, devices } from "@playwright/test";

// Run the WASM dev server before testing: make wasm (serves on :5000)
// Then: cd e2e && npm test

export default defineConfig({
  testDir: "./tests",
  outputDir: "./test-results",
  snapshotDir: "./snapshots",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: "list",
  use: {
    baseURL: process.env.BASE_URL ?? "http://localhost:5000",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
  ],
});
