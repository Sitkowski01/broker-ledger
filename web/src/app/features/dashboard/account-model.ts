import { BalanceDto, PortfolioDto } from '../../core/account/account-api';
import { formatCents, MINUS, parseCents } from '../../core/format/money';

/**
 * Rachunki panelu wartosci bez Angulara: struktura rachunku, stopa zwrotu, waluty NBP.
 * Procenty tez liczymy z calkowitych groszy — bez liczb zmiennoprzecinkowych w kwotach.
 */

export interface Slice {
  readonly key: 'shares' | 'cash' | 'blocked';
  readonly label: string;
  readonly value: string;
  readonly percent: string;
  /** Szerokosc paska w CSS, np. "72.3%". */
  readonly width: string;
}

/** Wolne i zablokowane zlotowki z GET /api/me/balances (brak wiersza = zero). */
export function cashOf(balances: readonly BalanceDto[]): { available: number; blocked: number } {
  const pln = balances.find((b) => b.asset === 'PLN');
  return { available: parseCents(pln?.available ?? '0') ?? 0, blocked: parseCents(pln?.blocked ?? '0') ?? 0 };
}

/** Udzial czesci w calosci z dokladnoscia do promila: "72,3%" i szerokosc paska "72.3%". */
export function shareOfTotal(part: number, total: number): { percent: string; width: string } {
  const perMille = total > 0 ? Math.round((part * 1000) / total) : 0;
  return { percent: `${Math.floor(perMille / 10)},${perMille % 10}%`, width: `${perMille / 10}%` };
}

/** Na co sklada sie wartosc rachunku: akcje, wolna gotowka i gotowka zablokowana pod zlecenia. */
export function accountStructure(sharesCents: number, availableCents: number, blockedCents: number): Slice[] {
  const total = sharesCents + availableCents + blockedCents;
  const slice = (key: Slice['key'], label: string, cents: number): Slice => ({
    key,
    label,
    value: formatCents(cents),
    ...shareOfTotal(cents, total),
  });
  return [
    slice('shares', 'Akcje', sharesCents),
    slice('cash', 'Gotówka', availableCents),
    slice('blocked', 'Pod zlecenia', blockedCents),
  ];
}

/** Stopa zwrotu w punktach bazowych (1/100 procenta) jako "+0,70%" / "−4,98%" — zapis jak przy kwotach. */
export function formatBasisPoints(bp: number): string {
  const text = `${formatCents(Math.abs(bp))}%`;
  return bp > 0 ? `+${text}` : bp < 0 ? `${MINUS}${text}` : text;
}

/** Wynik wzgledem kosztu zakupu; bez kosztu (pusty portfel) nie ma czego dzielic. */
export function returnOn(pnlCents: number, costCents: number): string | null {
  if (costCents <= 0) {
    return null;
  }
  const bp = Math.round((Math.abs(pnlCents) * 10_000) / costCents);
  return formatBasisPoints(pnlCents < 0 ? -bp : bp);
}

export type Trend = 'up' | 'down' | 'flat';

export function trendOf(value: string): Trend {
  const cents = parseCents(value) ?? 0;
  return cents > 0 ? 'up' : cents < 0 ? 'down' : 'flat';
}

export interface ValueView {
  readonly code: string;
  readonly whole: string;
  /** Grosze; null, gdy API przyslalo kwote, ktorej nie umiemy odczytac — pokazujemy ja wtedy w calosci. */
  readonly fraction: string | null;
  /** Inna waluta niz PLN to przeliczenie po kursie srednim — wartosc przyblizona. */
  readonly approx: boolean;
  /** "1 EUR = 4,2618 zł" albo numer tabeli NBP dla zlotowek. */
  readonly note: string;
}

/** Waluty, w ktorych da sie pokazac wartosc rachunku: zawsze PLN, reszta tylko gdy NBP dal kurs. */
export function currenciesOf(portfolio: PortfolioDto): string[] {
  return ['PLN', ...portfolio.totalValueIn.map((v) => v.currency)];
}

export function valueIn(portfolio: PortfolioDto, code: string): ValueView {
  const converted = portfolio.totalValueIn.find((v) => v.currency === code);
  const pln = code === 'PLN' || !converted;
  const amount = pln ? portfolio.summary.totalValue : converted.amount;
  const cents = parseCents(amount);
  // Nieczytelna kwota zostaje taka, jaka przyszla — "0,00" sugerowaloby pusty rachunek.
  const [whole, fraction] = cents === null ? [amount, null] : formatCents(cents).split(',');
  const mid = portfolio.rates?.mid[code];
  return {
    code: pln ? 'PLN' : code,
    whole,
    fraction,
    approx: !pln,
    note: !pln && mid ? `1 ${code} = ${mid.replace('.', ',')} zł` : tableLabel(portfolio),
  };
}

/** "NBP 198/A · 13.10" — z ktorej tabeli kursow pochodzi przeliczenie. */
export function tableLabel(portfolio: PortfolioDto): string {
  const rates = portfolio.rates;
  if (!rates) {
    return 'Kursy NBP niedostępne';
  }
  const [, month, day] = rates.effectiveDate.split('-');
  return `NBP ${rates.table.split('/').slice(0, 2).join('/')} · ${day}.${month}`;
}
