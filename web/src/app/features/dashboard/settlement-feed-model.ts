import { HistoryEntryDto } from '../../core/account/account-api';
import { tradeTitle, unitsOf, unitsText } from '../../core/account/documents';
import { timeOf } from '../../core/format/dates';
import { shortId } from '../../core/format/ids';

/** Co z zyciem zlecen widac na Pulpicie: transakcje, blokady i zwrot reszty — bez przelewow. */
const SHOWN = new Set(['purchase', 'sale', 'block', 'release']);

export interface FeedItem {
  readonly sequence: number;
  readonly time: string;
  readonly status: string;
  readonly tone: 'done' | 'wait' | 'back';
  readonly title: string;
  readonly detail: string;
}

/** Ostatnie zdarzenia zlecen klienta z ksiegi, najnowsze pierwsze. */
export function settlementFeed(entries: readonly HistoryEntryDto[], limit = 5): FeedItem[] {
  return entries
    .filter((e) => SHOWN.has(e.description))
    .slice(0, limit)
    .map((e) => {
      const order = e.orderId ? `Zlecenie ${shortId(e.orderId)}` : 'Zlecenie';
      // Blokada i zwrot przesuwaja te sama kwote miedzy kontami klienta — wystarczy pierwszy jego zapis.
      const mine = e.postings.find((p) => p.owner === 'client');
      const amount = mine ? `${unitsText(mine.asset, unitsOf(mine))}${mine.asset === 'PLN' ? ' zł' : ` ${mine.asset}`}` : '';
      const base = { sequence: e.sequence, time: timeOf(e.createdAt) };
      switch (e.description) {
        case 'purchase':
        case 'sale':
          return {
            ...base,
            status: 'Rozliczone',
            tone: 'done' as const,
            title: `${tradeTitle(e.postings) ?? e.description} · ${e.description === 'purchase' ? 'kupno' : 'sprzedaż'}`,
            detail: `${order} — transakcja z giełdy zaksięgowana`,
          };
        case 'block':
          return { ...base, status: 'Blokada', tone: 'wait' as const, title: `Zablokowano ${amount}`, detail: `${order} idzie na giełdę` };
        default:
          return { ...base, status: 'Zwolnione', tone: 'back' as const, title: `Wróciło ${amount}`, detail: `${order} — reszta blokady` };
      }
    });
}
