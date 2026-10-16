import { TestBed } from '@angular/core/testing';
import { PortfolioDto } from '../../core/account/account-api';
import { AccountValue } from './account-value';
import { plain, SAMPLE_PORTFOLIO, words } from './testing';

describe('AccountValue', () => {
  async function render(portfolio: PortfolioDto = SAMPLE_PORTFOLIO) {
    const fixture = TestBed.createComponent(AccountValue);
    fixture.componentRef.setInput('portfolio', portfolio);
    fixture.componentRef.setInput('available', 821436);
    fixture.componentRef.setInput('blocked', 354377);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const total = () => el.querySelector('.total')!;
    const pick = async (code: string) => {
      [...el.querySelectorAll<HTMLButtonElement>('.seg button')].find((b) => b.textContent === code)!.click();
      await fixture.whenStable();
    };
    return { fixture, el, total, pick };
  }

  it('shows where to start on an empty account and takes the client to the deposit field', async () => {
    const empty: PortfolioDto = {
      ...SAMPLE_PORTFOLIO,
      positions: [],
      closed: [],
      summary: { cash: '0.00', sharesValue: '0.00', costBasis: '0.00', unrealizedPnl: '0.00', realizedPnl: '0.00', totalValue: '0.00' },
    };
    const field = document.body.appendChild(Object.assign(document.createElement('input'), { id: 'amount' }));
    field.scrollIntoView = vi.fn();
    const view = await render(empty);

    expect(view.el.querySelector('.empty-title')?.textContent).toBe('Rachunek jest pusty');
    expect(view.el.querySelector('.structure')).toBeNull();
    view.el.querySelector<HTMLButtonElement>('.btn')!.click();
    expect(document.activeElement).toBe(field);
    field.remove();

    expect((await render()).el.querySelector('.empty')).toBeNull();
  });

  it('shows the value in zlotys with its NBP table', async () => {
    const view = await render();

    expect(plain(view.total().getAttribute('aria-label'))).toBe('42 446,53 PLN');
    expect(words(view.total())).toBe('42 446 ,53 PLN');
    expect(view.el.querySelector('.meta')?.textContent).toBe('NBP 198/A · 13.10');
    expect(view.el.getAttribute('role')).toBe('region');
  });

  it('converts to a picked currency and says it is approximate', async () => {
    const view = await render();
    await view.pick('EUR');

    expect(plain(view.total().getAttribute('aria-label'))).toBe('około 9 959,76 EUR');
    expect(view.el.querySelector('.approx')).not.toBeNull();
    expect(plain(view.el.querySelector('.meta')?.textContent)).toBe('1 EUR = 4,2618 zł');
    expect(view.el.querySelector('[aria-pressed="true"]')?.textContent).toBe('EUR');
  });

  it('goes back to zlotys when NBP stops converting the picked currency', async () => {
    const view = await render();
    await view.pick('USD');
    view.fixture.componentRef.setInput('portfolio', { ...SAMPLE_PORTFOLIO, totalValueIn: [], rates: null });
    await view.fixture.whenStable();

    expect(plain(view.total().getAttribute('aria-label'))).toBe('42 446,53 PLN');
    expect(view.el.querySelector('.seg')).toBeNull();
    expect(view.el.querySelector('.meta')?.textContent).toBe('Kursy NBP niedostępne');
  });

  it('shows the share result against the purchase cost', async () => {
    const view = await render();
    const pnl = view.el.querySelector('.pnl')!;

    expect(plain(pnl.textContent)).toBe('+212,00 zł · +0,70%');
    expect(pnl.classList).toContain('up');
  });

  it('colours a loss and hides the result for an account without shares', async () => {
    const loss = await render({ ...SAMPLE_PORTFOLIO, summary: { ...SAMPLE_PORTFOLIO.summary, unrealizedPnl: '-430.00' } });
    expect(loss.el.querySelector('.pnl')?.classList).toContain('down');
    TestBed.resetTestingModule();

    const empty = await render({ ...SAMPLE_PORTFOLIO, summary: { ...SAMPLE_PORTFOLIO.summary, costBasis: '0.00', unrealizedPnl: '0.00' } });
    expect(empty.el.querySelector('.pnl')).toBeNull();
  });

  it('describes the structure of the account for screen readers and in the legend', async () => {
    const view = await render();

    expect(view.el.querySelector('.bars')?.getAttribute('aria-label')).toBe(
      'Struktura rachunku: akcje 72,3%, gotówka 19,4%, pod zlecenia 8,3%',
    );
    expect([...view.el.querySelectorAll<HTMLElement>('.bars div')].map((d) => d.style.width)).toEqual(['72.3%', '19.4%', '8.3%']);
    expect([...view.el.querySelectorAll('dd')].map(words)).toEqual([
      '30 688,40 72,3%',
      '8 214,36 19,4%',
      '3 543,77 8,3%',
    ]);
  });

  it('shows an unreadable value as it came', async () => {
    const view = await render({ ...SAMPLE_PORTFOLIO, summary: { ...SAMPLE_PORTFOLIO.summary, totalValue: '42446.531' } });

    expect(view.total().getAttribute('aria-label')).toBe('42446.531 PLN');
    expect(view.el.querySelector('.fraction')).toBeNull();
  });
});
