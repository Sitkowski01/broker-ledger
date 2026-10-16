import fc from 'fast-check';
import { HistoryEntryDto } from '../../core/account/account-api';
import { MAX_TRANSFER_CENTS, recentTransfers, shareOf, transferError } from './transfer-model';

const entry = (description: string, change: string, sequence: number, createdAt = '2026-10-14T16:05:00Z'): HistoryEntryDto => ({
  id: `id-${sequence}`,
  sequence,
  description,
  createdAt,
  orderId: null,
  key: `k-${sequence}`,
  changes: [{ account: 'available', asset: 'PLN', change }],
  postings: [],
});

describe('transfer model', () => {
  it('validates a transfer before it reaches the server', () => {
    expect(transferError('deposit', null, 0)).toBe('Podaj kwotę większą od zera, np. 250,40.');
    expect(transferError('deposit', 0, 0)).toBe('Podaj kwotę większą od zera, np. 250,40.');
    expect(transferError('deposit', -100, 0)).toBe('Podaj kwotę większą od zera, np. 250,40.');
    expect(transferError('deposit', 1, 0)).toBeNull();
    expect(transferError('deposit', MAX_TRANSFER_CENTS, 0)).toBeNull();
    expect(transferError('deposit', MAX_TRANSFER_CENTS + 1, 0)).toBe('Jednorazowo najwyżej 1 000 000 000 000,00 zł.');
  });

  it('never lets a withdrawal take more than the free cash', () => {
    expect(transferError('withdrawal', 821436, 821436)).toBeNull();
    expect(transferError('withdrawal', 821437, 821436)).toBe('Możesz wypłacić najwyżej 8 214,36 zł — środki pod zleceniami zostają.');
    expect(transferError('withdrawal', 1, 0)).toBe('Nie masz wolnych środków do wypłaty.');
    // Wplata nie zalezy od salda.
    expect(transferError('deposit', 821437, 0)).toBeNull();
  });

  it('rounds a part of the free cash down to the grosz', () => {
    expect(shareOf(821436, 25)).toBe(205359);
    expect(shareOf(821436, 50)).toBe(410718);
    expect(shareOf(821436, 100)).toBe(821436);
    expect(shareOf(3, 50)).toBe(1);
    fc.assert(fc.property(fc.nat(10 ** 12), fc.integer({ min: 0, max: 100 }), (cash, p) => shareOf(cash, p) <= cash));
  });

  it('lists the latest deposits and withdrawals, skipping trades', () => {
    const now = new Date('2026-10-14T18:00:00Z');
    const rows = recentTransfers(
      [
        entry('withdrawal', '-250.40', 9, '2026-10-14T16:05:00Z'),
        entry('purchase', '-993.60', 8),
        entry('deposit', '+10000.00', 7, '2026-09-14T07:12:00Z'),
        entry('deposit', '+1.00', 6),
        entry('deposit', '+2.00', 5),
      ],
      now,
    );
    expect(rows).toEqual([
      { sequence: 9, label: 'Wypłata do banku', amount: '−250,40', trend: 'down', at: 'dziś, 18:05' },
      { sequence: 7, label: 'Wpłata z banku', amount: '+10 000,00', trend: 'up', at: '14.09, 09:12' },
      { sequence: 6, label: 'Wpłata z banku', amount: '+1,00', trend: 'up', at: 'dziś, 18:05' },
    ]);
    expect(recentTransfers([entry('deposit', '+1.00', 1)], now, 0)).toEqual([]);
  });

  it('shows zero for a transfer without a free zloty change', () => {
    const odd = { ...entry('deposit', '+1.00', 1), changes: [{ account: 'blocked' as const, asset: 'PLN', change: '+1.00' }] };
    expect(recentTransfers([odd], new Date())[0]).toMatchObject({ amount: '0,00', trend: 'flat' });
  });
});
