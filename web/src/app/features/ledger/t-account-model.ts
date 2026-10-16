import { HistoryEntryDto } from '../../core/account/account-api';
import { warsawDay } from '../../core/format/dates';
import { formatCents, parseCents } from '../../core/format/money';
import { shortId } from '../../core/format/ids';

/**
 * Konto T "Dostepne PLN" za dzis, jak w podreczniku ksiegowosci. Konto klienta to dla brokera zobowiazanie,
 * wiec rosnie po stronie Ma: otwarcie i wplywy po Ma, wydatki po Wn, a saldo koncowe domyka strone Wn.
 * Otwarcie liczymy wstecz: dzisiejsze saldo minus dzisiejsze zmiany — bez osobnego zapytania o wczoraj.
 */

export interface TLine {
  readonly label: string;
  readonly amount: string;
  readonly role: 'line' | 'opening' | 'closing';
}

export interface TAccount {
  readonly debit: readonly TLine[];
  readonly credit: readonly TLine[];
  readonly total: string;
  readonly closing: string;
  /** Dzis nie bylo ruchu na dostepnych zlotowkach. */
  readonly empty: boolean;
}

const LABELS: Readonly<Record<string, string>> = {
  deposit: 'wpłata',
  withdrawal: 'wypłata',
  block: 'blokada',
  release: 'zwolnienie',
  purchase: 'kupno',
  sale: 'sprzedaż',
};

/**
 * @param entries dokumenty od najnowszych (jak z API); bierzemy tylko dzisiejsze
 * @param availableCents biezace saldo dostepnych zlotowek (GET /api/me/balances)
 */
export function tAccount(entries: readonly HistoryEntryDto[], availableCents: number, now: Date): TAccount {
  const today = warsawDay(now);
  const debit: TLine[] = [];
  const credit: TLine[] = [];
  let net = 0;
  let inflow = 0;
  // Od najstarszych, zeby konto czytalo sie w kolejnosci zdarzen.
  for (const e of [...entries].reverse()) {
    if (warsawDay(new Date(e.createdAt)) !== today) {
      continue;
    }
    const change = e.changes
      .filter((c) => c.account === 'available' && c.asset === 'PLN')
      .reduce((sum, c) => sum + (parseCents(c.change) ?? 0), 0);
    if (change === 0) {
      continue;
    }
    net += change;
    inflow += Math.max(change, 0);
    const label = `${LABELS[e.description] ?? e.description}${e.orderId ? ` ${shortId(e.orderId, 4)}` : ''}`;
    (change > 0 ? credit : debit).push({ label, amount: formatCents(Math.abs(change)), role: 'line' });
  }
  const opening = availableCents - net;
  credit.unshift({ label: 'otwarcie', amount: formatCents(opening), role: 'opening' });
  debit.push({ label: 'saldo', amount: formatCents(availableCents), role: 'closing' });
  // Ma: otwarcie + wplywy; Wn: wydatki + saldo. Obie strony sa rowne z definicji salda.
  return { debit, credit, total: formatCents(opening + inflow), closing: formatCents(availableCents), empty: credit.length === 1 && debit.length === 1 };
}
