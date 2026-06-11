import { test, expect } from "@playwright/test";

// MyVirtualLibrary uses Uno WASM + SkiaRenderer — UI is drawn on a <canvas>.
// Run the dev server first: make wasm   (serves on http://localhost:5000)
const WASM_TIMEOUT = 30_000;

test.describe("MyVirtualLibrary (unauthenticated)", () => {
  test("WASM bootstraps and renders the login screen", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (err) => errors.push(err.message));

    await page.goto("/");

    await expect(page.locator(".uno-loader")).toBeHidden({ timeout: WASM_TIMEOUT });

    const canvas = page.locator("#uno-body canvas").first();
    await expect(canvas).toBeVisible({ timeout: 5_000 });
    const box = await canvas.boundingBox();
    expect(box?.width).toBeGreaterThan(200);
    expect(box?.height).toBeGreaterThan(200);

    expect(errors).toHaveLength(0);

    await page.screenshot({ path: "snapshots/login-baseline.png", fullPage: true });
  });

  test("API is reachable (OpenAPI endpoint)", async ({ request }) => {
    // Dev server exposes OpenAPI at /openapi/v1.json — anonymous, always present in dev
    const apiBase = process.env.API_URL ?? "http://localhost:5179";
    const response = await request.get(`${apiBase}/openapi/v1.json`);
    expect(response.ok()).toBeTruthy();
  });
});
