import { expect, test } from '@playwright/test';
import { goTo, login } from './helpers';

/** Tryb demo: cala sciezka klienta w przegladarce, bez serwera. */
test.describe('demo', () => {
  test.beforeEach(async ({ page }) => login(page, 'demo@broker-ledger.pl'));

  test('dashboard shows the account, its result and live settlements', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'Pulpit' })).toBeVisible();
    await expect(page.locator('.total')).toHaveAttribute('aria-label', /PLN$/);
    await expect(page.locator('figcaption')).toContainText('Wynik rachunku');
    await expect(page.locator('bl-portfolio-table tbody tr')).toHaveCount(4);
    await expect(page.locator('bl-settlement-feed li').first()).toBeVisible();
    await expect(page.locator('bl-ticker')).toContainText('Live');
  });

  test('a buy at the last price goes through the whole saga and lands in the ledger', async ({ page }) => {
    await goTo(page, 'Zlecenia');
    await page.getByRole('button', { name: 'Ostatnia' }).click();
    await page.locator('#quantity').fill('4');
    await expect(page.locator('.summary')).toContainText('Do zablokowania');
    await page.getByRole('button', { name: /^Kup 4 szt\. CDR$/ }).click();

    await expect(page.locator('bl-order-ticket p.done')).toContainText('przyjęte przez giełdę');
    // Gielda demo wykonuje zlecenie w dwoch transakcjach, potem wraca reszta blokady.
    await expect(page.getByRole('status').filter({ hasText: 'Zlecenie wykonane' })).toBeVisible();
    await expect(page.locator('bl-order-saga li.done')).toHaveCount(5);

    await goTo(page, 'Księga');
    // Najnowszy dokument to druga transakcja albo zwrot reszty blokady, jesli cena zdazyla spasc.
    await expect(page.locator('bl-ledger-doc').first()).toContainText(/Kupno|Zwolnienie/);
    await page.locator('bl-ledger-doc').nth(1).locator('button.sum').click();
    await expect(page.locator('bl-ledger-doc').nth(1)).toContainText('Zbilansowany');
    await expect(page.locator('bl-t-account')).toContainText('saldo');
  });

  test('the ticket refuses what the account cannot cover before anything is sent', async ({ page }) => {
    await goTo(page, 'Zlecenia');
    await expect(page.getByRole('button', { name: 'PKC' })).toBeDisabled();
    await page.getByRole('button', { name: 'Sprzedaż' }).click();
    await page.locator('#price').fill('10');
    await page.locator('#quantity').fill('100000');
    await expect(page.locator('#quantity-error')).toContainText('Masz tylko');
  });

  test('a deposit is booked once and shows up as the newest transfer', async ({ page }) => {
    await page.locator('#amount').fill('1 234,56');
    await page.getByRole('button', { name: /^Wpłać 1 234,56 zł/ }).click();
    await expect(page.locator('bl-transfer-panel p.done')).toContainText(/Wpłata 1\s234,56 zł zaksięgowana\./);
    await expect(page.locator('.recent li').first()).toContainText(/\+1\s234,56/);
  });
});
