import { expect, Page } from '@playwright/test';

/** Logowanie przez formularz. W demo dziala dowolny e-mail i haslo, na prawdziwym API — zalozone konto. */
export async function login(page: Page, email: string, password = 'correct horse battery', register = false): Promise<void> {
  await page.goto('/zaloguj');
  if (register) {
    await page.getByRole('button', { name: 'Nowe konto' }).click();
  }
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.locator('form button[type=submit]').click();
  await expect(page).toHaveURL(/\/$/);
}

/** Strona miesci sie w szerokosci okna — nic nie wystaje w bok (najczestszy blad na telefonie). */
export async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

/** Przejscie przez nawigacje glowna (linki o tej samej nazwie bywaja tez w powiadomieniach). */
export async function goTo(page: Page, name: 'Pulpit' | 'Zlecenia' | 'Księga'): Promise<void> {
  await page.getByRole('navigation', { name: 'Główna' }).getByRole('link', { name, exact: true }).click();
}
