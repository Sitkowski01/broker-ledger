import { EMPTY, expand, Observable, reduce } from 'rxjs';
import { AccountApi, HistoryEntryDto } from '../../core/account/account-api';
import { warsawDay } from '../../core/format/dates';

/** Konto T czyta dzisiejsze dokumenty strona po stronie, ale nie wiecej niz tyle stron (aktywny klient). */
export const TODAY_PAGES = 5;
const TODAY_PAGE_SIZE = 100;

/** Dzisiejsze dokumenty (i ewentualnie kilka starszych z ostatniej strony): kolejne strony, dopoki ostatni jest z dzisiaj. */
export function todayEntries(api: AccountApi, now: Date): Observable<HistoryEntryDto[]> {
  const today = warsawDay(now);
  let pages = 0;
  return api.history(TODAY_PAGE_SIZE).pipe(
    expand((page) => {
      pages++;
      const last = page.items.at(-1);
      const more = page.nextBefore !== null && last && warsawDay(new Date(last.createdAt)) === today && pages < TODAY_PAGES;
      return more ? api.history(TODAY_PAGE_SIZE, page.nextBefore) : EMPTY;
    }),
    reduce((all, page) => [...all, ...page.items], [] as HistoryEntryDto[]),
  );
}
