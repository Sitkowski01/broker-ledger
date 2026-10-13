import { HttpErrorResponse } from '@angular/common/http';

/** Blad API przetlumaczony na to, co ekran ma pokazac. */
export type ApiError =
  | { readonly kind: 'validation'; readonly fields: Readonly<Record<string, string>> }
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number; readonly message: string }
  | { readonly kind: 'offline' | 'unauthorized' | 'conflict' | 'not-found' | 'server'; readonly message: string };

/** Komunikaty po polsku dla pol, ktore API waliduje (API odpowiada po angielsku, dla programistow). */
const FIELD_MESSAGES: Readonly<Record<string, string>> = {
  email: 'Podaj poprawny adres e-mail.',
  password: 'Hasło musi mieć od 8 do 128 znaków.',
  amount: 'Podaj kwotę większą od zera, np. 250,40.',
  quantity: 'Podaj liczbę akcji.',
  limitPrice: 'Podaj cenę w złotych, np. 248,40.',
  symbol: 'Wybierz walor.',
};

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

function titleOf(body: unknown): string | undefined {
  return (body as { title?: string } | null)?.title;
}
