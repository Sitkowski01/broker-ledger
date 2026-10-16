import { HistoryPageDto } from '../../core/account/account-api';
import { DEPOSIT } from './testing';
import { TODAY_PAGES, todayEntries } from './today';
import { of } from 'rxjs';

describe('todayEntries', () => {
  const now = new Date('2026-10-16T20:00:00Z');
  const at = (sequence: number, createdAt: string) => ({ ...DEPOSIT, sequence, createdAt });

  function apiWith(pages: HistoryPageDto[]) {
    const calls: (number | null)[] = [];
    const api = { history: (_limit: number, before: number | null = null) => (calls.push(before), of(pages[calls.length - 1])) };
    return { api: api as never, calls };
  }

  it('reads further pages while the last document is still from today', async () => {
    const { api, calls } = apiWith([
      { items: [at(9, '2026-10-16T19:00:00Z'), at(8, '2026-10-16T08:00:00Z')], nextBefore: 8 },
      { items: [at(7, '2026-10-16T07:00:00Z'), at(6, '2026-10-15T07:00:00Z')], nextBefore: 6 },
    ]);
    const all = await new Promise((resolve) => todayEntries(api, now).subscribe(resolve));
    expect((all as { sequence: number }[]).map((e) => e.sequence)).toEqual([9, 8, 7, 6]);
    expect(calls).toEqual([null, 8]);
  });

  it('stops at the last page and after a few pages for a very busy client', async () => {
    const busy = Array.from({ length: 10 }, (_, i) => ({ items: [at(100 - i, '2026-10-16T12:00:00Z')], nextBefore: 100 - i }));
    const { api, calls } = apiWith(busy);
    await new Promise((resolve) => todayEntries(api, now).subscribe(resolve));
    expect(calls).toHaveLength(TODAY_PAGES);

    const last = apiWith([{ items: [at(1, '2026-10-16T12:00:00Z')], nextBefore: null }]);
    await new Promise((resolve) => todayEntries(last.api, now).subscribe(resolve));
    expect(last.calls).toEqual([null]);
  });
});
