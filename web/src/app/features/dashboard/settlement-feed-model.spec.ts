import { HistoryEntryDto } from '../../core/account/account-api';
import { settlementFeed } from './settlement-feed-model';
import { plain } from './testing';

const ORDER = '01920000-0000-7000-8000-00000000c41e';
const base = { id: 'x', orderId: ORDER, key: null, changes: [], createdAt: '2026-10-16T17:42:07Z' };

describe('settlement feed model', () => {
  it('shows trades, blocks and released rest, newest first, without transfers', () => {
    const entries: HistoryEntryDto[] = [
      { ...base, sequence: 5, description: 'release', postings: [{ owner: 'client', account: 'blocked', asset: 'PLN', side: 'debit', amount: '2.40' }] },
      {
        ...base, sequence: 4, description: 'purchase',
        postings: [
          { owner: 'client', account: 'blocked', asset: 'PLN', side: 'debit', amount: '997.48' },
          { owner: 'broker', account: 'bank', asset: 'PLN', side: 'credit', amount: '993.60' },
          { owner: 'broker', account: 'depository', asset: 'CDR', side: 'debit', amount: '4' },
        ],
      },
      { ...base, sequence: 3, description: 'deposit', orderId: null, postings: [] },
      { ...base, sequence: 2, description: 'block', postings: [{ owner: 'client', account: 'blocked', asset: 'ALE', side: 'credit', amount: '50' }] },
      { ...base, sequence: 1, description: 'sale', orderId: null, postings: [] },
    ];

    expect(plain(JSON.stringify(settlementFeed(entries).map((f) => [f.status, f.tone, f.title, f.detail])))).toBe(plain(JSON.stringify([
      ['Zwolnione', 'back', 'Wróciło 2,40 zł', 'Zlecenie 0000c41e — reszta blokady'],
      ['Rozliczone', 'done', 'CDR · 4 szt. × 248,40 zł · kupno', 'Zlecenie 0000c41e — transakcja z giełdy zaksięgowana'],
      ['Blokada', 'wait', 'Zablokowano 50 szt. ALE', 'Zlecenie 0000c41e idzie na giełdę'],
      ['Rozliczone', 'done', 'sale · sprzedaż', 'Zlecenie — transakcja z giełdy zaksięgowana'],
    ])));
    expect(settlementFeed(entries, 2)).toHaveLength(2);
    expect(settlementFeed(entries)[0].time).toBe('19:42:07');
  });
});
