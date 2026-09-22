import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const playwrightPath = path.resolve(process.env.USERPROFILE, '.cache', 'codex-runtimes', 'codex-primary-runtime', 'dependencies', 'node', 'node_modules', 'playwright', 'index.mjs');
const { chromium } = await import(pathToFileURL(playwrightPath).href);

const output = path.resolve('reports/worker-tests/end-to-end.json');
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
const consoleErrors = [];
page.on('console', message => {
  if (message.type() === 'error') consoleErrors.push(message.text());
});

try {
  await page.goto('http://127.0.0.1:4173/', { waitUntil: 'domcontentloaded' });
  await page.getByLabel('Name').fill('E2E Synthetic');
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText('Saved E2E Synthetic').waitFor();
  await page.getByRole('button', { name: 'Deliberate failure' }).click();
  await page.getByText('Failure detected').waitFor();
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText('Saved E2E Synthetic').waitFor();
  if (!consoleErrors.includes('intentional fixture error')) throw new Error('Expected deliberate failure was not observed');

  const evidence = {
    status: 'PASS',
    checks: ['navigate', 'fill', 'save', 'detect deliberate failure', 'recover and save'],
    consoleErrors,
    url: page.url()
  };
  fs.writeFileSync(output, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify(evidence));
} finally {
  await browser.close();
}
