import { HistoryEntryDto } from '../../core/account/account-api';
import { plain } from '../dashboard/testing';
import { byDay, docView, matchesSearch } from './ledger-model';
import { BLOCK, DAY, DEPOSIT, PURCHASE, RELEASE } from './testing';

const texts = <T>(value: T): T => JSON.parse(plain(JSON.stringify(value))) as T;

describe('ledger model', () => {
  it('shows a purchase with the price per share and both sides of every asset', () => {
    expect(texts(docView(PURCHASE))).toEqual({
      sequence: 3,
      kind: 'Kupno',
      tone: 'buy',
      title: 'CDR · 4 szt. × 248,40 zł',
      amount: '997,48 zł',
      key: PURCHASE.key,
      orderId: PURCHASE.orderId,
      createdAt: PURCHASE.createdAt,
      rows: [
        { mine: true, owner: 'Twoje', name: 'Zablokowane', asset: 'PLN', debit: '997,48', credit: '' },
        { mine: false, owner: 'Broker', name: 'Bank', asset: 'PLN', debit: '', credit: '993,60' },
        { mine: false, owner: 'Broker', name: 'Przychód z prowizji', asset: 'PLN', debit: '', credit: '3,88' },
        { mine: false, owner: 'Broker', name: 'Depozyt', asset: 'CDR', debit: '4 szt.', credit: '' },
        { mine: true, owner: 'Twoje', name: 'Dostępne', asset: 'CDR', debit: '', credit: '4 szt.' },
      ],
      sums: ['PLN 997,48 = 997,48', 'CDR 4 = 4'],
      balanced: true,
    });
  });

  it('describes transfers, blocks and releases', () => {
    expect(texts([DEPOSIT, BLOCK, RELEASE].map((e) => [docView(e).kind, docView(e).title, docView(e).amount]))).toEqual([
      ['Wpłata', 'Przelew z banku', '10 000,00 zł'],
      ['Blokada', 'Pod kupno · zlecenie 0000c41e', '999,88 zł'],
      ['Zwolnienie', 'Reszta blokady wraca · zlecenie 0000c41e', '2,40 zł'],
    ]);
    expect(docView({ ...DEPOSIT, description: 'withdrawal' }).title).toBe('Przelew do banku');
  });

  it('describes a sale and blocked shares', () => {
    const shares = (description: string, side: 'debit' | 'credit'): HistoryEntryDto => ({
      ...BLOCK,
      description,
      postings: [
        { owner: 'client', account: 'available', asset: 'ALE', side, amount: '50' },
        { owner: 'client', account: 'blocked', asset: 'ALE', side: side === 'debit' ? 'credit' : 'debit', amount: '50' },
      ],
    });
    expect(docView(shares('block', 'debit'))).toMatchObject({ title: 'Pod sprzedaż 50 szt. ALE · zlecenie 0000c41e', amount: '50 szt.' });
    expect(docView(shares('release', 'credit')).title).toBe('Niesprzedane 50 szt. ALE wracają · zlecenie 0000c41e');

    const sale: HistoryEntryDto = {
      ...PURCHASE,
      description: 'sale',
      postings: [
        { owner: 'client', account: 'blocked', asset: 'CDR', side: 'debit', amount: '3' },
        { owner: 'broker', account: 'depository', asset: 'CDR', side: 'credit', amount: '3' },
        { owner: 'broker', account: 'bank', asset: 'PLN', side: 'debit', amount: '100.00' },
      ],
    };
    // 100 zl za 3 akcje to nie cena w pelnych groszach — wtedy bez ceny, zamiast zaokraglac.
    expect(texts(docView(sale))).toMatchObject({ kind: 'Sprzedaż', tone: 'sell', title: 'CDR · 3 szt.', amount: '3 szt.' });
  });

  it('says when part of a document is on another client account', () => {
    const gift: HistoryEntryDto = { ...DEPOSIT, description: 'transfer', postings: [DEPOSIT.postings[1]] };
    expect(texts(docView(gift))).toMatchObject({ title: 'transfer', kind: 'transfer', tone: 'other', balanced: false, sums: ['PLN 0,00 ≠ 10 000,00'] });
    expect(docView({ ...gift, postings: [] }).amount).toBe('—');
    expect(docView({ ...PURCHASE, postings: [] }).title).toBe('Kupno');
  });

  it('groups documents by Warsaw day, newest first', () => {
    const now = new Date('2026-10-16T20:00:00Z');
    const yesterday = { ...DEPOSIT, sequence: 0, createdAt: '2026-10-15T22:30:00Z' };
    const older = { ...DEPOSIT, sequence: -1, createdAt: '2026-10-10T08:00:00Z' };
    // 22:30 UTC 15.10 to juz 00:30 16.10 w Warszawie (CEST).
    expect(byDay([...DAY, yesterday, older].map(docView), now).map((d) => [d.label, d.docs.length])).toEqual([
      ['Dziś · 16.10.2026', 5],
      ['10.10.2026', 1],
    ]);
    expect(byDay([{ ...DEPOSIT, createdAt: '2026-10-15T12:00:00Z' }].map(docView), now)[0].label).toBe('Wczoraj · 15.10.2026');
  });

  it('finds documents by order, key, company or number', () => {
    const docs = DAY.map(docView);
    const find = (q: string) => docs.filter((d) => matchesSearch(d, q)).map((d) => d.sequence);
    expect(find('')).toEqual([4, 3, 2, 1]);
    expect(find('  cdr ')).toEqual([3]);
    expect(find('01920000')).toEqual([4, 3, 2]);
    expect(find('dep-01')).toEqual([1]);
    expect(find('#2')).toEqual([2]);
    expect(find('wpłata')).toEqual([1]);
    expect(find('xyz')).toEqual([]);
  });
});
