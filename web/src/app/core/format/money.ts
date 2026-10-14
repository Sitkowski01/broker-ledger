/**
 * Kwoty jak w ksiedze: bez liczb zmiennoprzecinkowych. API podaje kwoty jako tekst ("8214.36"),
 * a rachunki we froncie (prowizja, blokada) ida na calkowitych groszach.
 */

const NBSP = ' ';
/** Prawdziwy minus (U+2212), nie dywiz — tak jak w wyciagach. */
export const MINUS = '−';

/** "8214.36" -> 821436 groszy. Odrzuca wszystko, co nie jest kwota z najwyzej dwoma miejscami po przecinku. */
export function parseCents(value: string): number | null {
  const match = /^([+-]?)(\d+)(?:[.,](\d{1,2}))?$/.exec(value.trim().replace(/[\s ]/g, ''));
  if (!match) {
    return null;
  }
  const cents = Number(match[2]) * 100 + Number((match[3] ?? '').padEnd(2, '0'));
  return Number.isSafeInteger(cents) ? (match[1] === '-' ? -cents : cents) : null;
}

/** 821436 -> "8 214,36": spacja co trzy cyfry (takze w tysiacach) i przecinek, jak w polskich wyciagach. */
export function formatCents(cents: number): string {
  const sign = cents < 0 ? MINUS : '';
  const abs = Math.abs(cents);
  return `${sign}${formatQuantity(Math.floor(abs / 100))},${String(abs % 100).padStart(2, '0')}`;
}

/** Liczba calkowita z odstepem co trzy cyfry (np. ilosc akcji): 1200 -> "1 200". */
export function formatQuantity(value: number): string {
  return String(value).replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);
}

/** Kwota z API do wyswietlenia; nieczytelna wartosc zostaje taka, jaka przyszla, zamiast udawac zero. */
export function formatPrice(value: string): string {
  const cents = parseCents(value);
  return cents === null ? value : formatCents(cents);
}

/** Ze znakiem: "+212,00", "−430,00" (prawdziwy minus, nie dywiz). */
export function formatSigned(value: string): string {
  const cents = parseCents(value);
  if (cents === null) {
    return value;
  }
  return cents > 0 ? `+${formatCents(cents)}` : formatCents(cents);
}

/** Grosze z powrotem do formatu API ("250.40"). */
export function toApiAmount(cents: number): string {
  const abs = Math.abs(cents);
  return `${cents < 0 ? '-' : ''}${Math.floor(abs / 100)}.${String(abs % 100).padStart(2, '0')}`;
}
