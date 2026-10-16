import { plain } from '../dashboard/testing';
import { tAccount } from './t-account-model';
import { BLOCK, DAY, DEPOSIT, RELEASE } from './testing';

const texts = <T>(value: T): T => JSON.parse(plain(JSON.stringify(value))) as T;
const now = new Date('2026-10-16T20:00:00Z');

describe('T account', () => {
  it('puts inflows on Ma, outflows on Wn and closes with the current balance', () => {
    // Dzis: wplata 10 000, blokada 999,88, zwrot 2,40 → saldo 9 002,52; otwarcie 0.
    expect(texts(tAccount(DAY, 900_252, now))).toEqual({
      credit: [
        { label: 'otwarcie', amount: '0,00', role: 'opening' },
        { label: 'wpłata', amount: '10 000,00', role: 'line' },
        { label: 'zwolnienie c41e', amount: '2,40', role: 'line' },
      ],
      debit: [
        { label: 'blokada c41e', amount: '999,88', role: 'line' },
        { label: 'saldo', amount: '9 002,52', role: 'closing' },
      ],
      total: '10 002,40',
      closing: '9 002,52',
      empty: false,
    });
  });

  it('works the opening balance back from today, ignoring older documents', () => {
    const yesterday = { ...DEPOSIT, createdAt: '2026-10-15T10:00:00Z' };
    const account = texts(tAccount([RELEASE, BLOCK, yesterday], 1_000_000 - 99_988 + 240, now));
    expect(account.credit[0]).toEqual({ label: 'otwarcie', amount: '10 000,00', role: 'opening' });
    expect(account.total).toBe('10 002,40');
  });

  it('shows an empty day as opening equal to the balance', () => {
    expect(texts(tAccount([], 821_436, now))).toMatchObject({
      credit: [{ label: 'otwarcie', amount: '8 214,36', role: 'opening' }],
      debit: [{ label: 'saldo', amount: '8 214,36', role: 'closing' }],
      total: '8 214,36',
      empty: true,
    });
  });

  it('skips documents that do not touch free zlotys (a purchase pays from the block)', () => {
    const shares = { ...BLOCK, description: 'sale', orderId: null, changes: [{ account: 'available' as const, asset: 'CDR', change: '-5' }] };
    expect(tAccount([shares], 100, now).empty).toBe(true);
    const odd = { ...DEPOSIT, description: 'transfer' };
    expect(tAccount([odd], 1_000_000, now).credit[1].label).toBe('transfer');
  });
});
