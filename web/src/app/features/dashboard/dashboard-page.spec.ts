import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PortfolioDto } from '../../core/account/account-api';
import { CLOCK_TICK_MS, DashboardPage } from './dashboard-page';
import { plain, SAMPLE_PORTFOLIO } from './testing';

describe('DashboardPage', () => {
  let backend: HttpTestingController;

  const balances = [{ asset: 'PLN', kind: 'Cash', available: '8214.36', blocked: '3543.77' }];
  const history = {
    items: [{ id: 'a', sequence: 2, description: 'deposit', createdAt: '2026-10-14T16:05:00Z', orderId: null, changes: [{ account: 'available', asset: 'PLN', change: '+10000.00' }] }],
    nextBefore: null,
  };

  async function render() {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DashboardPage);
    // Nie czekamy na whenStable: zapytania httpResource wisza, dopoki test na nie nie odpowie.
    TestBed.tick();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, stable: () => fixture.whenStable() };
  }

  /** Odpowiada na trzy zapytania Pulpitu; brak odpowiedzi = zapytanie dalej wisi. */
  function respond(portfolio: PortfolioDto = SAMPLE_PORTFOLIO) {
    TestBed.tick();
    backend.expectOne('/api/me/portfolio').flush(portfolio);
    backend.expectOne('/api/me/balances').flush(balances);
    const page: TestRequest = backend.expectOne((r) => r.url === '/api/me/history');
    // Same przelewy z serwera: aktywny klient z wieloma zleceniami tez zobaczy ostatnie wplaty.
    expect([page.request.params.get('limit'), page.request.params.get('kind')]).toEqual(['3', 'transfers']);
    page.flush(history);
  }

  afterEach(() => backend.verify());

  it('waits for the account before showing numbers', async () => {
    const page = await render();

    expect(page.el.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(page.el.querySelector('bl-account-value')).toBeNull();
    respond();
  });

  it('shows the value, the transfer panel and the portfolio from the API', async () => {
    const page = await render();
    respond();
    await page.stable();

    expect(page.el.querySelector('[aria-busy="true"]')).toBeNull();
    expect(plain(page.el.querySelector('.total')?.getAttribute('aria-label'))).toBe('42 446,53 PLN');
    expect(plain(page.el.querySelector('.structure dd')?.textContent)).toContain('30 688,40');
    expect(page.el.querySelectorAll('bl-portfolio-table tbody tr')).toHaveLength(3);
    expect(plain(page.el.querySelector('.recent li')?.textContent)).toContain('+10 000,00');
    expect(page.el.querySelector('.kicker')?.textContent).toMatch(/^Rachunek maklerski · \S+ \d{2}\.\d{2}\.\d{4}$/);
  });

  it('refreshes the whole account after a transfer', async () => {
    const page = await render();
    respond();
    await page.stable();

    page.el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await page.stable();
    backend.expectOne('/api/me/deposits').flush({ outcome: 'posted' }, { status: 201, statusText: 'Created' });

    respond({ ...SAMPLE_PORTFOLIO, summary: { ...SAMPLE_PORTFOLIO.summary, totalValue: '43446.53' } });
    await page.stable();
    expect(plain(page.el.querySelector('.total')?.getAttribute('aria-label'))).toBe('43 446,53 PLN');
  });

  it('warns when the NBP rates are old', async () => {
    const page = await render();
    respond({ ...SAMPLE_PORTFOLIO, rates: { ...SAMPLE_PORTFOLIO.rates!, stale: true } });
    await page.stable();

    expect(plain(page.el.querySelector('.alert.warn')?.textContent)).toContain('ostatniej pobranej tabeli 198/A/NBP/2026');
  });

  it('explains a failed load and tries again on request', async () => {
    const page = await render();
    TestBed.tick();
    backend.expectOne('/api/me/portfolio').flush(null, { status: 503, statusText: 'Unavailable' });
    backend.expectOne('/api/me/balances').flush(balances);
    backend.expectOne((r) => r.url === '/api/me/history').flush(history);
    await page.stable();

    const alert = page.el.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain('Serwer chwilowo nie odpowiada.');
    expect(page.el.querySelector('[aria-busy="true"]')).toBeNull();

    alert.querySelector('button')!.click();
    respond();
    await page.stable();
    expect(page.el.querySelector('[role="alert"]')).toBeNull();
    expect(page.el.querySelector('bl-account-value')).not.toBeNull();
  });

  it('still opens when the ledger history is unavailable', async () => {
    const page = await render();
    TestBed.tick();
    backend.expectOne('/api/me/portfolio').flush(SAMPLE_PORTFOLIO);
    backend.expectOne('/api/me/balances').flush(balances);
    backend.expectOne((r) => r.url === '/api/me/history').flush(null, { status: 500, statusText: 'Server Error' });
    await page.stable();

    expect(page.el.querySelector('bl-account-value')).not.toBeNull();
    expect(page.el.querySelector('.recent')).toBeNull();
    expect(page.el.querySelector('[role="alert"]')).toBeNull();
  });

  it('names a dropped connection', async () => {
    const page = await render();
    TestBed.tick();
    backend.expectOne('/api/me/portfolio').flush(null, { status: 0, statusText: 'Unknown Error' });
    backend.expectOne('/api/me/balances').flush(balances);
    backend.expectOne((r) => r.url === '/api/me/history').flush(history);
    await page.stable();

    expect(page.el.querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia z serwerem.');
  });

  it('keeps the account and the transfer confirmation when the refresh after a transfer fails', async () => {
    const page = await render();
    respond();
    await page.stable();

    page.el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await page.stable();
    backend.expectOne('/api/me/deposits').flush({ outcome: 'posted' }, { status: 201, statusText: 'Created' });
    TestBed.tick();
    backend.expectOne('/api/me/portfolio').flush(null, { status: 429, statusText: 'Too Many Requests' });
    backend.expectOne('/api/me/balances').flush(balances);
    backend.expectOne((r) => r.url === '/api/me/history').flush(history);
    await page.stable();

    expect(page.el.querySelector('[role="alert"]')?.textContent).toContain('Za dużo prób');
    expect(plain(page.el.querySelector('.total')?.getAttribute('aria-label'))).toBe('42 446,53 PLN');
    expect(page.el.querySelector('.done')?.textContent).toContain('zaksięgowana');
  });

  it('moves the date on when the page stays open past midnight', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-10-13T21:59:30Z'));
    try {
      const page = await render();
      respond();
      await page.stable();
      expect(page.el.querySelector('.kicker')?.textContent).toContain('wtorek 13.10.2026');

      vi.advanceTimersByTime(CLOCK_TICK_MS);
      await page.stable();
      expect(page.el.querySelector('.kicker')?.textContent).toContain('środa 14.10.2026');
    } finally {
      vi.useRealTimers();
    }
  });
});
