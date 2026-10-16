import { HistoryEntryDto, PostingDto } from '../../core/account/account-api';

const ORDER = '01920000-0000-7000-8000-00000000c41e';

const client = (account: PostingDto['account'], asset: string, side: PostingDto['side'], amount: string): PostingDto =>
  ({ owner: 'client', account, asset, side, amount });
const broker = (account: PostingDto['account'], asset: string, side: PostingDto['side'], amount: string): PostingDto =>
  ({ owner: 'broker', account, asset, side, amount });

/** Dzien z makiety Ksiegi: wplata, blokada pod kupno 4 CDR, transakcja i zwrot reszty blokady (najnowsze pierwsze). */
export const DEPOSIT: HistoryEntryDto = {
  id: 'd', sequence: 1, description: 'deposit', createdAt: '2026-10-16T16:05:00Z', orderId: null, key: 'dep-01',
  changes: [{ account: 'available', asset: 'PLN', change: '+10000.00' }],
  postings: [broker('bank', 'PLN', 'debit', '10000.00'), client('available', 'PLN', 'credit', '10000.00')],
};

export const BLOCK: HistoryEntryDto = {
  id: 'b', sequence: 2, description: 'block', createdAt: '2026-10-16T17:42:02Z', orderId: ORDER, key: `order:${ORDER}:block`,
  changes: [{ account: 'available', asset: 'PLN', change: '-999.88' }, { account: 'blocked', asset: 'PLN', change: '+999.88' }],
  postings: [client('available', 'PLN', 'debit', '999.88'), client('blocked', 'PLN', 'credit', '999.88')],
};

export const PURCHASE: HistoryEntryDto = {
  id: 'p', sequence: 3, description: 'purchase', createdAt: '2026-10-16T17:42:07Z', orderId: ORDER, key: `order:${ORDER}:trade:1/CDR/412`,
  changes: [{ account: 'blocked', asset: 'PLN', change: '-997.48' }, { account: 'available', asset: 'CDR', change: '+4' }],
  postings: [
    client('blocked', 'PLN', 'debit', '997.48'),
    broker('bank', 'PLN', 'credit', '993.60'),
    broker('feeIncome', 'PLN', 'credit', '3.88'),
    broker('depository', 'CDR', 'debit', '4'),
    client('available', 'CDR', 'credit', '4'),
  ],
};

export const RELEASE: HistoryEntryDto = {
  id: 'r', sequence: 4, description: 'release', createdAt: '2026-10-16T17:42:07Z', orderId: ORDER, key: `order:${ORDER}:release`,
  changes: [{ account: 'blocked', asset: 'PLN', change: '-2.40' }, { account: 'available', asset: 'PLN', change: '+2.40' }],
  postings: [client('blocked', 'PLN', 'debit', '2.40'), client('available', 'PLN', 'credit', '2.40')],
};

export const DAY = [RELEASE, PURCHASE, BLOCK, DEPOSIT];
