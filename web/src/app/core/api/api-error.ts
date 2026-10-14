import { HttpErrorResponse } from '@angular/common/http';
import { formatPrice, formatQuantity } from '../format/money';

/** Blad API przetlumaczony na to, co ekran ma pokazac. */
export type ApiError =
  | { readonly kind: 'validation'; readonly fields: Readonly<Record<string, string>> }
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number; readonly message: string }
  | { readonly kind: 'offline' | 'unauthorized' | 'conflict' | 'not-found' | 'insufficient' | 'rejected' | 'server'; readonly message: string };

/** Komunikaty po polsku dla pol, ktore API waliduje (API odpowiada po angielsku, dla programistow). */
const FIELD_MESSAGES: Readonly<Record<string, string>> = {
  email: 'Podaj poprawny adres e-mail.',
  password: 'Hasło musi mieć od 8 do 128 znaków.',
  amount: 'Podaj kwotę większą od zera, np. 250,40.',
  quantity: 'Podaj liczbę akcji.',
  limitPrice: 'Podaj cenę w złotych, np. 248,40.',
  symbol: 'Wybierz walor.',
};

/** Tytul odpowiedzi API, gdy ksiega nie pozwala zejsc z saldem ponizej zera (LedgerExceptionHandler). */
const INSUFFICIENT = 'Insufficient funds';

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'server', message: 'Coś poszło nie tak. Spróbuj ponownie.' };
  }
  switch (error.status) {
    case 0:
      return { kind: 'offline', message: 'Brak połączenia z serwerem. Sprawdź internet i spróbuj ponownie.' };
    case 400:
      return { kind: 'validation', fields: fieldsOf(error.error) };
    case 401:
      return { kind: 'unauthorized', message: 'Nieprawidłowy e-mail lub hasło.' };
    case 404:
      return { kind: 'not-found', message: 'Nie znaleziono.' };
    case 409:
      return { kind: 'conflict', message: titleOf(error.error) ?? 'Konflikt z aktualnym stanem.' };
    case 422:
      // 422 to dwa rozne przypadki: ksiega odmowila, bo saldo zeszloby ponizej zera (np. pieniadze wydane
      // w drugiej karcie), albo gielda odrzucila zlecenie — wtedy w tresci jest zlecenie, nie brak srodkow.
      return titleOf(error.error) === INSUFFICIENT
        ? { kind: 'insufficient', message: insufficientMessage(error.error) }
        : { kind: 'rejected', message: 'Operacja odrzucona.' };
    case 429: {
      const seconds = Number(error.headers.get('Retry-After')) || 60;
      return { kind: 'rate-limited', retryAfterSeconds: seconds, message: `Za dużo prób. Spróbuj ponownie za ${seconds} s.` };
    }
    default:
      return { kind: 'server', message: 'Serwer chwilowo nie odpowiada. Spróbuj za chwilę.' };
  }
}

function fieldsOf(body: unknown): Record<string, string> {
  const errors = (body as { errors?: Record<string, string[]> } | null)?.errors ?? {};
  return Object.fromEntries(
    Object.entries(errors).map(([field, messages]) => [field, FIELD_MESSAGES[field] ?? messages[0] ?? 'Niepoprawna wartość.']),
  );
}

function insufficientMessage(body: unknown): string {
  const { asset, available } = (body as { asset?: string; available?: string } | null) ?? {};
  if (asset === 'PLN' && available) {
    return `Za mało wolnych środków. Dostępne: ${formatPrice(available)} zł.`;
  }
  return asset && available ? `Za mało akcji ${asset}. Dostępne: ${quantityText(available)} szt.` : 'Za mało wolnych środków.';
}

function quantityText(value: string): string {
  return /^\d+$/.test(value) ? formatQuantity(Number(value)) : value;
}

function titleOf(body: unknown): string | undefined {
  return (body as { title?: string } | null)?.title;
}
