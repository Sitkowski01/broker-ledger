import fc from 'fast-check';
import {
  accountStructure,
  currenciesOf,
  formatBasisPoints,
  returnOn,
  shareOfTotal,
  tableLabel,
  trendOf,
  valueIn,
} from './account-model';
import { SAMPLE_PORTFOLIO as portfolio } from './testing';

describe('account model', () => {
  it('splits the account into shares, cash and blocked cash to a tenth of a percent', () => {
    expect(accountStructure(3068840, 821436, 354377)).toEqual([
      { key: 'shares', label: 'Akcje', value: '30 688,40', percent: '72,3%', width: '72.3%' },
      { key: 'cash', label: 'Gotówka', value: '8 214,36', percent: '19,4%', width: '19.4%' },
      { key: 'blocked', label: 'Pod zlecenia', value: '3 543,77', percent: '8,3%', width: '8.3%' },
    ]);
  });

  it('shows an empty account as zeros instead of dividing by zero', () => {
    expect(accountStructure(0, 0, 0).map((s) => s.percent)).toEqual(['0,0%', '0,0%', '0,0%']);
    expect(shareOfTotal(5, 0)).toEqual({ percent: '0,0%', width: '0%' });
    expect(shareOfTotal(100, 100)).toEqual({ percent: '100,0%', width: '100%' });
  });

  it('keeps every share between 0 and 100 percent', () => {
    fc.assert(
      fc.property(fc.nat(10 ** 12), fc.nat(10 ** 12), (part, rest) => {
        const width = parseFloat(shareOfTotal(part, part + rest).width);
        return width >= 0 && width <= 100;
      }),
    );
  });

  it('formats returns with a sign and two decimals, grouped like amounts', () => {
    expect(formatBasisPoints(70)).toBe('+0,70%');
    expect(formatBasisPoints(-498)).toBe('−4,98%');
    expect(formatBasisPoints(0)).toBe('0,00%');
    expect(formatBasisPoints(1205)).toBe('+12,05%');
    expect(formatBasisPoints(123456)).toBe('+1 234,56%');
  });

  it('computes the return on cost symmetrically for gains and losses', () => {
    expect(returnOn(21200, 3047640)).toBe('+0,70%');
    expect(returnOn(-43000, 864000)).toBe('−4,98%');
    expect(returnOn(0, 100)).toBe('0,00%');
    expect(returnOn(100, 0)).toBeNull();
    expect(returnOn(100, -5)).toBeNull();
  });

  it.each([
    ['212.00', 'up'],
    ['-0.01', 'down'],
    ['0.00', 'flat'],
    ['n/a', 'flat'],
  ])('trend of %s is %s', (value, trend) => {
    expect(trendOf(value)).toBe(trend);
  });

  it('offers PLN and every currency NBP converted to', () => {
    expect(currenciesOf(portfolio)).toEqual(['PLN', 'EUR', 'USD']);
    expect(currenciesOf({ ...portfolio, totalValueIn: [], rates: null })).toEqual(['PLN']);
  });

  it('shows the value in zlotys with the NBP table it would convert by', () => {
    expect(valueIn(portfolio, 'PLN')).toEqual({ code: 'PLN', whole: '42 446', fraction: '53', approx: false, note: 'NBP 198/A · 13.10' });
  });

  it('marks a converted value as approximate and gives the rate', () => {
    expect(valueIn(portfolio, 'EUR')).toEqual({ code: 'EUR', whole: '9 959', fraction: '76', approx: true, note: '1 EUR = 4,2618 zł' });
  });

  it('falls back to zlotys for a currency NBP no longer converts', () => {
    expect(valueIn(portfolio, 'GBP')).toMatchObject({ code: 'PLN', whole: '42 446', approx: false });
    const noMid = { ...portfolio, rates: { ...portfolio.rates!, mid: {} } };
    expect(valueIn(noMid, 'EUR')).toMatchObject({ approx: true, note: 'NBP 198/A · 13.10' });
  });

  it('shows an unreadable amount as it came instead of pretending the account is empty', () => {
    const odd = { ...portfolio, summary: { ...portfolio.summary, totalValue: '42446.531' } };
    expect(valueIn(odd, 'PLN')).toMatchObject({ whole: '42446.531', fraction: null });
  });

  it('says when there are no rates at all', () => {
    expect(tableLabel({ ...portfolio, rates: null })).toBe('Kursy NBP niedostępne');
  });
});
