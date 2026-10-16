/**
 * Krotki numer do opisow: ostatnie 8 znakow UUID. Nie poczatek — UUID v7 zaczyna sie od znacznika czasu,
 * wiec zlecenia z tej samej chwili mialyby ten sam "krotki numer". Koniec jest losowy.
 */
export function shortId(id: string, length = 8): string {
  return id.replace(/-/g, '').slice(-length);
}
