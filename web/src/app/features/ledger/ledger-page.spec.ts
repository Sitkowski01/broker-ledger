import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HistoryEntryDto } from '../../core/account/account-api';
import { plain, words } from '../dashboard/testing';
import { LedgerPage, PAGE_SIZE } from './ledger-page';
import { BLOCK, DAY, DEPOSIT } from './testing';

describe('LedgerPage', () => {
  let backend: HttpTestingController;

  const balances = [{ asset: 'PLN', kind: 'Cash', available: '9002.52', blocked: '997.48' }];

  async function render() {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T20:00:00Z'));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LedgerPage);
    TestBed.tick();
    const el = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const click = async (label: string) => {
      [...el.querySelectorAll('button')].find((b) => plain(b.textContent).startsWith(label))!.click();
      await stable();
    };
    const titles = () => [...el.querySelectorAll('bl-ledger-doc .title')].map((t) => plain(t.textContent));
    return { fixture, el, stable, click, titles };
  }

  /** Zapytania listy (limit 20) i konta T (limit 100) rozpoznajemy po rozmiarze strony. */
  const listRequest = () => backend.expectOne((r) => r.url === '/api/me/history' && r.params.get('limit') === String(PAGE_SIZE));
  const page = (items: HistoryEntryDto[], nextBefore: number | null = null) => ({ items, nextBefore });

  function respond(items = DAY, nextBefore: number | null = null) {
    TestBed.tick();
    listRequest().flush(page(items, nextBefore));
    backend.expectOne((r) => r.url === '/api/me/history' && r.params.get('limit') === '100').flush(page(DAY));
    backend.expectOne('/api/me/balances').flush(balances);
  }

  afterEach(() => {
    vi.useRealTimers();
    backend.verify();
  });

  it('shows the documents of the day with the newest one open', async () => {
    const p = await render();
    respond();
    await p.stable();

    expect(p.el.querySelector('.day h2')?.textContent).toBe('Dziś · 16.10.2026');
    expect(p.titles()).toEqual(['Reszta blokady wraca · zlecenie 0000c41e', 'CDR · 4 szt. × 248,40 zł', 'Pod kupno · zlecenie 0000c41e', 'Przelew z banku']);
    const open = p.el.querySelectorAll('bl-ledger-doc table');
    expect(open).toHaveLength(1);
    expect(words(p.el.querySelector('bl-ledger-doc .foot'))).toContain('Zbilansowany PLN 2,40 = 2,40 #4');
  });

  it('builds the T account from today and the current balance', async () => {
    const p = await render();
    respond();
    await p.stable();

    const t = words(p.el.querySelector('bl-t-account'));
    expect(t).toContain('Wn blokada c41e 999,88 saldo 9 002,52');
    expect(t).toContain('Ma otwarcie 0,00 wpłata 10 000,00 zwolnienie c41e 2,40');
    expect(t).toContain('Razem 10 002,40 Razem 10 002,40');
  });

  it('opens and closes documents one by one or all at once', async () => {
    const p = await render();
    respond();
    await p.stable();

    await p.click('Rozwiń wszystkie');
    expect(p.el.querySelectorAll('bl-ledger-doc table')).toHaveLength(4);
    await p.click('Zwiń wszystkie');
    expect(p.el.querySelectorAll('bl-ledger-doc table')).toHaveLength(0);

    p.el.querySelectorAll<HTMLButtonElement>('bl-ledger-doc .sum')[1].click();
    await p.stable();
    expect(p.el.querySelectorAll('bl-ledger-doc .sum')[1].getAttribute('aria-expanded')).toBe('true');
    expect(words(p.el.querySelector('bl-ledger-doc table'))).toContain('Broker Przychód z prowizji PLN 3,88');
  });

  it('asks the API for one kind of documents and starts over', async () => {
    const p = await render();
    respond();
    await p.stable();

    await p.click('Blokady');
    const blocks: TestRequest = listRequest();
    expect(blocks.request.params.get('kind')).toBe('blocks');
    expect(blocks.request.params.has('before')).toBe(false);
    blocks.flush(page([BLOCK]));
    await p.stable();
    expect(p.titles()).toEqual(['Pod kupno · zlecenie 0000c41e']);
    expect(p.el.querySelector('[aria-pressed="true"]')?.textContent).toContain('Blokady');
  });

  it('loads older documents with the cursor and appends them', async () => {
    const p = await render();
    respond(DAY.slice(0, 2), 3);
    await p.stable();

    await p.click('Wczytaj starsze');
    const older = listRequest();
    expect(older.request.params.get('before')).toBe('3');
    older.flush(page(DAY.slice(2)));
    await p.stable();
    expect(p.titles()).toHaveLength(4);
    expect(p.el.textContent).not.toContain('Wczytaj starsze');
  });

  it('searches the loaded documents', async () => {
    const p = await render();
    respond();
    await p.stable();

    const input = p.el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'dep-01';
    input.dispatchEvent(new Event('input'));
    await p.stable();
    expect(p.titles()).toEqual(['Przelew z banku']);

    input.value = 'nic takiego';
    input.dispatchEvent(new Event('input'));
    await p.stable();
    expect(p.el.querySelector('.empty')?.textContent).toContain('nie pasuje do „nic takiego”');
  });

  it('keeps what was loaded and offers a retry when the next page fails', async () => {
    const p = await render();
    respond([DEPOSIT], 1);
    await p.stable();

    await p.click('Wczytaj starsze');
    listRequest().flush(null, { status: 0, statusText: 'Unknown Error' });
    await p.stable();
    expect(p.el.querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia');
    expect(p.titles()).toEqual(['Przelew z banku']);

    // Bez czekania na whenStable: zapytania wisza, dopoki test na nie nie odpowie.
    [...p.el.querySelectorAll('button')].find((b) => b.textContent?.includes('Spróbuj ponownie'))!.click();
    TestBed.tick();
    const retry = listRequest();
    expect(retry.request.params.get('before')).toBe('1');
    retry.flush(page([BLOCK]));
    backend.expectOne('/api/me/balances').flush(balances);
    await p.stable();
    expect(p.titles()).toEqual(['Przelew z banku', 'Pod kupno · zlecenie 0000c41e']);
  });
});
