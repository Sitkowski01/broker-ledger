import { formatCents, formatQuantity } from '../../core/format/money';
import { OrderSide, OrderType } from '../../core/orders/order-api';

/**
 * Rachunki formularza zlecenia bez Angulara — te same co w domenie (Order.BuyBlock, Commission.Standard),
 * na calkowitych groszach. Front liczy dla wygody; o blokadzie i tak decyduje ksiega.
 */

/** Commission.Standard: 0,39% w gore do grosza, nie mniej niz 3 zl. */
export const COMMISSION_BP = 39;
export const MIN_COMMISSION_CENTS = 300;
/** Order.MaxQuantity i Order.MaxPrice na Parkiecie. */
export const MAX_QUANTITY = 1_000_000_000;
export const MAX_PRICE_CENTS = 1_000_000_000;

/** Prowizja od wartosci transakcji w groszach — zaokraglona w gore, jak w Commission.For. */
export function commissionCents(valueCents: number): number {
  const fee = Math.floor((valueCents * COMMISSION_BP + 9_999) / 10_000);
  return Math.max(fee, MIN_COMMISSION_CENTS);
}

/** Ile zablokuje kupno: wartosc po limicie plus prowizja (Order.BuyBlock). */
export function buyBlockCents(limitCents: number, quantity: number): number {
  const value = limitCents * quantity;
  return value + commissionCents(value);
}

/** Najwiecej akcji, na ktore starczy wolnych srodkow razem z prowizja. */
export function maxBuyQuantity(availableCents: number, limitCents: number): number {
  if (limitCents <= 0 || availableCents < buyBlockCents(limitCents, 1)) {
    return 0;
  }
  // Szacunek z gory (prowizja procentowa), potem w dol, dopoki nie zmiesci sie razem z minimum prowizji.
  let quantity = Math.min(Math.floor((availableCents * 10_000) / (limitCents * (10_000 + COMMISSION_BP))) + 1, MAX_QUANTITY);
  while (quantity > 0 && buyBlockCents(limitCents, quantity) > availableCents) {
    quantity--;
  }
  return quantity;
}

/** Ilosc akcji z pola: tylko liczba calkowita (spacje co trzy cyfry dozwolone). */
export function parseQuantity(text: string): number | null {
  const digits = text.replace(/[\s ]/g, '');
  if (!/^\d+$/.test(digits)) {
    return null;
  }
  const value = Number(digits);
  return Number.isSafeInteger(value) ? value : null;
}

export interface TicketInput {
  readonly side: OrderSide;
  readonly type: OrderType;
  readonly limitCents: number | null;
  readonly quantity: number | null;
  /** Wolne zlotowki w groszach. */
  readonly availableCents: number;
  /** Wolne akcje tej spolki. */
  readonly heldShares: number;
}

export interface TicketErrors {
  readonly price: string | null;
  readonly quantity: string | null;
  /** Brakuje pieniedzy albo akcji — to pokazujemy od razu, zanim klient kliknie. */
  readonly short: boolean;
}

/** Bledy pol formularza; oba null = zlecenie mozna wyslac. */
export function ticketErrors(t: TicketInput): TicketErrors {
  let price: string | null = null;
  if (t.type === 'limit') {
    if (t.limitCents === null || t.limitCents <= 0) {
      price = 'Podaj limit w złotych, np. 248,40.';
    } else if (t.limitCents > MAX_PRICE_CENTS) {
      price = `Limit najwyżej ${formatCents(MAX_PRICE_CENTS)} zł.`;
    }
  }
  let quantity: string | null = null;
  let short = false;
  if (t.quantity === null || t.quantity < 1) {
    quantity = 'Podaj liczbę akcji, co najmniej 1.';
  } else if (t.quantity > MAX_QUANTITY) {
    quantity = `Najwyżej ${formatQuantity(MAX_QUANTITY)} akcji w jednym zleceniu.`;
  } else if (t.side === 'sell' && t.quantity > t.heldShares) {
    short = true;
    quantity = t.heldShares > 0 ? `Masz tylko ${formatQuantity(t.heldShares)} szt. do sprzedaży.` : 'Nie masz akcji tej spółki do sprzedaży.';
  } else if (t.side === 'buy' && price === null && t.limitCents !== null) {
    const need = buyBlockCents(t.limitCents, t.quantity);
    if (need > t.availableCents) {
      short = true;
      quantity = `Brakuje ${formatCents(need - t.availableCents)} zł — zmniejsz ilość albo limit.`;
    }
  }
  return { price, quantity, short };
}

export interface TicketSummary {
  /** Wartosc po limicie (przy PKC po ostatniej cenie, jako szacunek); null, gdy nie ma z czego liczyc. */
  readonly valueCents: number | null;
  readonly commissionCents: number | null;
  /** Kupno: zlotowki do zablokowania. Sprzedaz: blokujemy akcje, nie pieniadze. */
  readonly blockCents: number | null;
  /** Kwoty przy PKC to szacunek — cena wyjdzie dopiero na gieldzie. */
  readonly estimated: boolean;
}

export function ticketSummary(t: TicketInput, lastCents: number | null): TicketSummary {
  const price = t.type === 'limit' ? t.limitCents : lastCents;
  const quantity = t.quantity ?? 0;
  if (price === null || price <= 0 || quantity < 1) {
    return { valueCents: null, commissionCents: null, blockCents: null, estimated: t.type === 'market' };
  }
  const value = price * quantity;
  const fee = commissionCents(value);
  return {
    valueCents: value,
    commissionCents: fee,
    blockCents: t.side === 'buy' ? value + fee : null,
    estimated: t.type === 'market',
  };
}

export interface SummaryRows {
  readonly value: string;
  readonly commission: string;
  readonly blockLabel: string;
  readonly block: string;
  readonly afterLabel: string;
  readonly after: string;
}

/** Podsumowanie pod formularzem: co zablokujemy i co zostanie. Kwoty PKC ze znakiem "≈". */
export function summaryRows(t: TicketInput, lastCents: number | null): SummaryRows {
  const s = ticketSummary(t, lastCents);
  const money = (cents: number | null) => (cents === null ? '—' : `${s.estimated ? '≈ ' : ''}${formatCents(cents)} zł`);
  const quantity = t.quantity ?? 0;
  if (t.side === 'buy') {
    return {
      value: money(s.valueCents),
      commission: money(s.commissionCents),
      blockLabel: 'Do zablokowania',
      block: money(s.blockCents),
      afterLabel: 'Zostanie dostępne',
      after: s.blockCents === null ? '—' : `${formatCents(Math.max(0, t.availableCents - s.blockCents))} zł`,
    };
  }
  return {
    value: money(s.valueCents),
    commission: money(s.commissionCents),
    blockLabel: 'Blokada akcji',
    block: quantity > 0 ? `${formatQuantity(quantity)} szt.` : '—',
    afterLabel: 'Wpływ po prowizji',
    after: s.valueCents === null ? '—' : money(Math.max(0, s.valueCents - (s.commissionCents ?? 0))),
  };
}

/** Napis na przycisku: "Kup 10 szt. CDR". */
export function ctaText(side: OrderSide, quantity: number | null, symbol: string): string {
  const verb = side === 'buy' ? 'Kup' : 'Sprzedaj';
  return quantity && quantity > 0 ? `${verb} ${formatQuantity(quantity)} szt. ${symbol}` : verb;
}
