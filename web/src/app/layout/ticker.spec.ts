import { TestBed } from '@angular/core/testing';
import { Ticker, TickerItem } from './ticker';

describe('Ticker', () => {
  async function render(items: readonly TickerItem[]) {
    const fixture = TestBed.createComponent(Ticker);
    fixture.componentRef.setInput('items', items);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('is hidden while there are no quotes', async () => {
    expect((await render([])).hidden).toBe(true);
  });

  it('repeats the quotes into an even loop and reads them once to screen readers', async () => {
    const el = await render([
      { label: 'CDR', value: '248,40', mark: '▲' },
      { label: 'EUR/PLN', value: '4,2618', mark: 'NBP' },
    ]);

    const tape = el.querySelectorAll('.item');
    expect(tape.length % 2).toBe(0);
    expect(tape.length).toBeGreaterThanOrEqual(16);
    expect(el.querySelector('.sr-only')?.textContent).toBe('Notowania: CDR 248,40, EUR/PLN 4,2618');
    expect(tape[0].querySelector('.up')?.textContent).toBe('▲');
    expect(tape[1].querySelector('.note')?.textContent).toBe('NBP');
  });
});
