import { HistoryEntryDto, TransferKind } from '../../core/account/account-api';
import { shortDateTime } from '../../core/format/dates';
import { formatCents, formatSigned } from '../../core/format/money';
import { Trend, trendOf } from './account-model';

/** Gorna granica jednej operacji w ksiedze (Posting.MaxAmount w domenie): bilion zlotych. */
export const MAX_TRANSFER_CENTS = 100_000_000_000_000;

/** Komunikat pod polem kwoty albo null, gdy przelew mozna wyslac. */
export function transferError(kind: TransferKind, cents: number | null, availableCents: number): string | null {
  if (cents === null || cents <= 0) {
    return 'Podaj kwotę większą od zera, np. 250,40.';
  }
  if (cents > MAX_TRANSFER_CENTS) {
    return `Jednorazowo najwyżej ${formatCents(MAX_TRANSFER_CENTS)} zł.`;
  }
  if (kind === 'withdrawal' && cents > availableCents) {
    return availableCents > 0
      ? `Możesz wypłacić najwyżej ${formatCents(availableCents)} zł — środki pod zleceniami zostają.`
      : 'Nie masz wolnych środków do wypłaty.';
  }
  return null;
}

/** Czesc wolnych srodkow do wyplaty, zaokraglona w dol do grosza — nigdy wiecej niz jest. */
export function shareOf(availableCents: number, percent: number): number {
  return Math.floor((availableCents * percent) / 100);
}

export interface TransferRow {
  readonly sequence: number;
  readonly label: string;
  readonly amount: string;
  readonly trend: Trend;
  readonly at: string;
}

/** Ostatnie wplaty i wyplaty z historii ksiegi, najnowsze pierwsze. */
export function recentTransfers(entries: readonly HistoryEntryDto[], now: Date, limit = 3): TransferRow[] {
  return entries
    .filter((e) => e.description === 'deposit' || e.description === 'withdrawal')
    .slice(0, limit)
    .map((e) => {
      const change = e.changes.find((c) => c.asset === 'PLN' && c.account === 'available')?.change ?? '0';
      return {
        sequence: e.sequence,
        label: e.description === 'deposit' ? 'Wpłata z banku' : 'Wypłata do banku',
        amount: formatSigned(change),
        trend: trendOf(change),
        at: shortDateTime(e.createdAt, now),
      };
    });
}
