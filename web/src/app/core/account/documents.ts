import { formatCents, formatQuantity, parseCents } from '../format/money';
import { PostingDto } from './account-api';

const PLN = 'PLN';

/** Kwota zapisu jako liczba calkowita: grosze dla zlotowek, sztuki dla akcji. */
export function unitsOf(p: Pick<PostingDto, 'asset' | 'amount'>): number {
  return p.asset === PLN ? (parseCents(p.amount) ?? 0) : Number(p.amount) || 0;
}

/** "1 034,02" dla zlotowek, "4 szt." dla akcji. */
export function unitsText(asset: string, units: number): string {
  return asset === PLN ? formatCents(units) : `${formatQuantity(units)} szt.`;
}

/**
 * "CDR · 4 szt. × 248,40 zł" z zapisow transakcji: cena to zapis banku (wartosc bez prowizji) przez liczbe akcji
 * z depozytu. Gdy nie wychodzi w pelnych groszach — bez ceny, zamiast zaokraglac. Null, gdy to nie transakcja.
 */
export function tradeTitle(postings: readonly PostingDto[]): string | null {
  const shares = postings.find((p) => p.account === 'depository');
  const bank = postings.find((p) => p.account === 'bank');
  if (!shares || !bank) {
    return null;
  }
  const quantity = unitsOf(shares);
  const value = unitsOf(bank);
  const price = quantity > 0 && value % quantity === 0 ? ` × ${formatCents(value / quantity)} zł` : '';
  return `${shares.asset} · ${unitsText(shares.asset, quantity)}${price}`;
}
