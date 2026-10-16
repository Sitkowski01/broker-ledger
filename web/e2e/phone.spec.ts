import { expect, test } from '@playwright/test';
import { expectNoHorizontalScroll, goTo, login } from './helpers';

/** Telefon (390 px): kazdy ekran miesci sie w szerokosci i da sie po nim przejsc. */
test('every screen fits a phone', async ({ page }) => {
  await login(page, 'demo@broker-ledger.pl');
  await expectNoHorizontalScroll(page);
  for (const name of ['Zlecenia', 'Księga', 'Pulpit'] as const) {
    await goTo(page, name);
    await expect(page.getByRole('heading', { name, level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  }
});
