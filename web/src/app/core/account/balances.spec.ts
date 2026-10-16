import { cashOf, sharesOf } from './balances';

describe('balances', () => {
  const balances = [
    { asset: 'CDR', kind: 'Shares', available: '6', blocked: '4' },
    { asset: 'PLN', kind: 'Cash', available: '8214.36', blocked: '3543.77' },
  ] as const;

  it('reads free and blocked zlotys, treating a missing row as zero', () => {
    expect(cashOf(balances)).toEqual({ available: 821436, blocked: 354377 });
    expect(cashOf([])).toEqual({ available: 0, blocked: 0 });
  });

  it('reads free shares of every company', () => {
    expect(sharesOf(balances)).toEqual({ CDR: 6 });
    expect(sharesOf([])).toEqual({});
  });
});
