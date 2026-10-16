import { expect, request, test } from '@playwright/test';
import { goTo, login } from './helpers';

/**
 * Prawdziwy system: API, PostgreSQL, Kafka i gielda Parkiet z botami. Uruchamiane z E2E_LIVE=1, gdy API slucha
 * na 8091 (front laczy sie z nim przez proxy). Kazdy test zaklada nowe konto.
 */
const api = 'http://localhost:8091';
const password = 'correct horse battery';

test('a new client deposits, places an order and sees the saga, the ledger and live signals', async ({ page }) => {
  await login(page, `e2e-${Date.now()}@example.com`, password, true);

  // Pusty rachunek mowi, od czego zaczac, i prowadzi do wplaty.
  await expect(page.getByText('Rachunek jest pusty')).toBeVisible();
  await page.getByRole('button', { name: 'Wpłać pierwsze środki' }).click();
  await expect(page.locator('#amount')).toBeFocused();
  await page.locator('#amount').fill('10 000');
  await page.getByRole('button', { name: /^Wpłać 10\s000,00 zł/ }).click();
  await expect(page.locator('bl-transfer-panel p.done')).toContainText('zaksięgowana');
  await expect(page.locator('bl-ticker')).toContainText('Live');

  await goTo(page, 'Zlecenia');
  await page.getByRole('button', { name: 'Ostatnia' }).click();
  await page.locator('#quantity').fill('1');
  await page.getByRole('button', { name: /^Kup 1 szt\./ }).click();
  await expect(page.locator('bl-order-ticket p.done')).toContainText('Zablokowano');
  // Blokada i przyjecie przez gielde widac na osi bez odswiezania strony (SSE).
  await expect(page.locator('bl-order-saga li.done')).toHaveCount(3, { timeout: 30_000 });

  await goTo(page, 'Księga');
  await expect(page.locator('bl-ledger-doc').filter({ hasText: 'Blokada' })).toHaveCount(1);
  await expect(page.locator('bl-ledger-doc').filter({ hasText: 'Wpłata' })).toHaveCount(1);
});

test('a deposit made elsewhere appears on the open dashboard without a reload', async ({ page }) => {
  const email = `e2e-sse-${Date.now()}@example.com`;
  await login(page, email, password, true);
  await expect(page.locator('bl-ticker')).toContainText('Live');

  const http = await request.newContext({ baseURL: api });
  const login2 = await http.post('/api/auth/login', { data: { email, password } });
  const token = ((await login2.json()) as { accessToken: string }).accessToken;
  await http.post('/api/me/deposits', {
    data: { amount: '500.00', currency: 'PLN' },
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': 'elsewhere' },
  });

  await expect(page.locator('.total')).toHaveAttribute('aria-label', /^500,00 PLN$/);
  await http.dispose();
});
