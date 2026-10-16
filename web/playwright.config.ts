import { defineConfig, devices } from '@playwright/test';

/**
 * Testy e2e w przegladarce. Dwa projekty:
 * - demo: front w trybie demo (symulator w przegladarce) — bez serwera, deterministycznie, zawsze w CI;
 * - live: front przez proxy na prawdziwym API (PostgreSQL, Kafka, gielda Parkiet) — gdy E2E_LIVE=1.
 * Lokalnie na Windowsie Edge z systemu (pobieranie Chromium bywa zablokowane), w CI Chromium Playwrighta.
 */
const live = process.env['E2E_LIVE'] === '1';
// Gotowy front (np. nginx z docker compose na 8093) zamiast serwera deweloperskiego z proxy.
const liveUrl = process.env['E2E_LIVE_URL'];
const browser = process.env['CI'] ? devices['Desktop Chrome'] : { ...devices['Desktop Edge'], channel: 'msedge' };

export default defineConfig({
  testDir: 'e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: { trace: 'retain-on-failure', screenshot: 'only-on-failure', locale: 'pl-PL', timezoneId: 'Europe/Warsaw' },
  projects: [
    { name: 'demo', testMatch: /demo\.spec\.ts/, use: { ...browser, baseURL: 'http://localhost:4310' } },
    { name: 'demo-phone', testMatch: /phone\.spec\.ts/, use: { ...browser, viewport: { width: 390, height: 844 }, baseURL: 'http://localhost:4310' } },
    ...(live ? [{ name: 'live', testMatch: /live\.spec\.ts/, use: { ...browser, baseURL: liveUrl ?? 'http://localhost:4311' } }] : []),
  ],
  webServer: [
    { command: 'npx ng serve --configuration demo --port 4310', url: 'http://localhost:4310', reuseExistingServer: !process.env['CI'], timeout: 180_000 },
    ...(live && !liveUrl
      ? [{ command: 'npx ng serve --port 4311', url: 'http://localhost:4311', reuseExistingServer: !process.env['CI'], timeout: 180_000 }]
      : []),
  ],
});
