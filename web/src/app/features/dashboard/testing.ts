import { PortfolioDto } from '../../core/account/account-api';

/** Rachunek z makiety Pulpitu: 3 walory, czesc gotowki pod zleceniami, kursy NBP z tabeli 198/A. */
export const SAMPLE_PORTFOLIO: PortfolioDto = {
  positions: [
    { symbol: 'CDR', quantity: 60, averageCost: '240.94', costBasis: '14456.40', lastPrice: '248.40', priceAt: '2026-10-13T17:42:07Z', marketValue: '14904.00', unrealizedPnl: '447.60', returnPercent: '3.10', realizedPnl: '0.00' },
    { symbol: 'ALE', quantity: 200, averageCost: '43.20', costBasis: '8640.00', lastPrice: '41.05', priceAt: '2026-10-13T17:39:12Z', marketValue: '8210.00', unrealizedPnl: '-430.00', returnPercent: '-4.98', realizedPnl: '0.00' },
    { symbol: 'PKN', quantity: 1200, averageCost: '61.50', costBasis: '7380.00', lastPrice: null, priceAt: null, marketValue: '7574.40', unrealizedPnl: '0.00', returnPercent: null, realizedPnl: '0.00' },
  ],
  closed: [],
  summary: { cash: '11758.13', sharesValue: '30688.40', costBasis: '30476.40', unrealizedPnl: '212.00', realizedPnl: '0.00', totalValue: '42446.53' },
  totalValueIn: [{ currency: 'EUR', amount: '9959.76' }, { currency: 'USD', amount: '11521.24' }],
  rates: { source: 'NBP', table: '198/A/NBP/2026', effectiveDate: '2026-10-13', stale: false, mid: { EUR: '4.2618', USD: '3.6842' } },
};

/** Tekst bez twardych spacji — czytelniejsze oczekiwania w testach widokow. */
export function plain(text: string | null | undefined): string {
  return (text ?? '').replace(/ /g, ' ').replace(/\s+/g, ' ').trim();
}

/** Tekst elementu z odstepem miedzy wezlami — Angular usuwa biale znaki miedzy znacznikami, CSS robi odstepy. */
export function words(el: Element | null | undefined): string {
  if (!el) {
    return '';
  }
  const walker = el.ownerDocument.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  const parts: string[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    parts.push(node.textContent ?? '');
  }
  return plain(parts.join(' '));
}
