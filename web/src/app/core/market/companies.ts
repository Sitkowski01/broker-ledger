/** Spolki notowane na Parkiecie (application.yml gieldy). API podaje tylko symbol — nazwa jest dla ludzi. */
const COMPANIES: Readonly<Record<string, string>> = {
  CDR: 'CD Projekt',
  PKO: 'PKO BP',
  PKN: 'Orlen',
  PZU: 'PZU',
  KGH: 'KGHM',
  ALE: 'Allegro',
};

/** Nazwa spolki; nowy symbol, ktorego front jeszcze nie zna, pokazuje sie po prostu jako symbol. */
export function companyName(symbol: string): string {
  return COMPANIES[symbol] ?? symbol;
}
