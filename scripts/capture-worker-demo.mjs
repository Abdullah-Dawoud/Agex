import { mkdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));
const playwrightPath = resolve(process.env.USERPROFILE, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "node", "node_modules", "playwright", "index.mjs");
const { chromium } = await import(pathToFileURL(playwrightPath).href);
const output = resolve(root, "reports", "worker-tests");
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1280, height: 720 },
  recordVideo: { dir: output, size: { width: 1280, height: 720 } },
});
const page = await context.newPage();
await page.goto("http://127.0.0.1:4173/", { waitUntil: "domcontentloaded" });
await page.screenshot({ path: resolve(output, "fixture-worker.png"), fullPage: true });
await page.getByLabel("Name").fill("Synthetic Demo");
await page.getByRole("button", { name: "Save" }).click();
await page.getByText("Saved Synthetic Demo").waitFor();
await page.waitForTimeout(500);
await page.screenshot({ path: resolve(output, "fixture-demo-final.png"), fullPage: true });
await page.close();
await context.close();
await browser.close();
console.log("Browser demo capture complete");
