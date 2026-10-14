/**
 * Klucz idempotencji dla operacji, ktora mozna bezpiecznie ponowic (przelew, zlecenie): losowy UUID v4.
 * Bez crypto.randomUUID, bo to dziala tylko na HTTPS i localhost — a front otwiera sie tez z telefonu po IP w sieci.
 */
export function newIdempotencyKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // wersja 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // wariant RFC 4122
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
