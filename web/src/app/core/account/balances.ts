import { parseCents } from '../format/money';
import { BalanceDto } from './account-api';

/** Wolne i zablokowane zlotowki z GET /api/me/balances (brak wiersza = zero). */
export function cashOf(balances: readonly BalanceDto[]): { available: number; blocked: number } {
  const pln = balances.find((b) => b.asset === 'PLN');
  return { available: parseCents(pln?.available ?? '0') ?? 0, blocked: parseCents(pln?.blocked ?? '0') ?? 0 };
}

/** Wolne akcje kazdej spolki (symbol -> sztuki) — tyle mozna sprzedac. */
export function sharesOf(balances: readonly BalanceDto[]): Record<string, number> {
  return Object.fromEntries(balances.filter((b) => b.kind === 'Shares').map((b) => [b.asset, Number(b.available) || 0]));
}
