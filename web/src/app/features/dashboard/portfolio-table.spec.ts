import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PortfolioDto } from '../../core/account/account-api';
import { PortfolioTable } from './portfolio-table';
import { plain, SAMPLE_PORTFOLIO, words } from './testing';

describe('PortfolioTable', () => {
  // Ostatnia transakcja byla 13.10 — 'dzis' w tescie to 14.10, niezaleznie od dnia uruchomienia.
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-14T08:00:00Z'));
  });

  afterEach(() => vi.useRealTimers());

  async function render(portfolio: PortfolioDto = SAMPLE_PORTFOLIO) {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(PortfolioTable);
    fixture.componentRef.setInput('portfolio', portfolio);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const cells = (row: Element) => [...row.querySelectorAll('th, td')].map(words);

  it('lists every open position with its price, share and result', async () => {
    const el = await render();
    const rows = el.querySelectorAll('tbody tr');

    expect(plain(el.querySelector('.count')?.textContent)).toBe('3 walory');
    expect(rows).toHaveLength(3);
    expect(cells(rows[0])).toEqual(['CDR CD Projekt CDR · Parkiet', '60', '240,94', '248,40 13.10, 19:42', '14 904,00', '48,6%', '▲ +447,60 +3,10%', '']);
    expect(cells(rows[1])).toEqual(['ALE Allegro ALE · Parkiet', '200', '43,20', '41,05 13.10, 19:39', '8 210,00', '26,8%', '▼ −430,00 −4,98%', '']);
    expect(rows[1].querySelector('.pnl')?.classList).toContain('down');
  });

  it('copes with a position that has not traded yet', async () => {
    const el = await render();
    const row = el.querySelectorAll('tbody tr')[2];

    expect(cells(row)).toEqual(['PKN Orlen PKN · Parkiet', '1 200', '61,50', '— brak transakcji', '7 574,40', '24,7%', '0,00', '']);
    expect(row.querySelector('.pnl')?.classList).toContain('flat');
  });

  it('sums the portfolio in the footer', async () => {
    const el = await render();

    expect(cells(el.querySelector('tfoot tr')!)).toEqual(['Razem', '', '30 476,40', '', '30 688,40', '100%', '▲ +212,00 +0,70%', '']);
  });

  it('links each position to a new order for that company', async () => {
    const el = await render();
    const trade = el.querySelector<HTMLAnchorElement>('.trade')!;

    expect(trade.getAttribute('href')).toBe('/zlecenia?walor=CDR');
    expect(trade.getAttribute('aria-label')).toBe('Handluj: CD Projekt');
    expect(el.querySelectorAll('.mob a')).toHaveLength(3);
  });

  it('invites to the first order when there are no shares', async () => {
    const el = await render({ ...SAMPLE_PORTFOLIO, positions: [] });

    expect(el.querySelector('table')).toBeNull();
    expect(plain(el.querySelector('.count')?.textContent)).toBe('0 walorów');
    expect(el.querySelector('.empty a')?.getAttribute('href')).toBe('/zlecenia');
  });
});
