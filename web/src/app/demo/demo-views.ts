import { BalanceDto, HistoryKind, HistoryPageDto, PerformancePointDto, PortfolioDto } from '../core/account/account-api';
import { parseCents, toApiAmount } from '../core/format/money';
import { QuoteDto } from '../core/market/quote-feed';
import { DemoLedger } from './demo-ledger';
import { DEMO_SYMBOLS, DemoMarket } from './demo-market';

/**
 * Odczyty trybu demo w ksztalcie odpowiedzi API: salda, historia, portfel, wynik i notowania.
 * Wszystko liczone z ksiegi demo i cen rynku — tak jak serwer liczy z PostgreSQL i skrzynki Kafki.
 */

const PLN = 'PLN';
export const DAY = 86_400_000;
const KINDS: Readonly<Record<HistoryKind, readonly string[]>> = {
  transfers: ['deposit', 'withdrawal'],
  trades: ['purchase', 'sale'],
  blocks: ['block', 'release'],
};
/** Kursy srednie NBP z tabeli 202/A/NBP/2026 (16.10.2026). */
const NBP: Readonly<Record<string, number>> = { EUR: 4.2618, USD: 3.6842, GBP: 4.9031, CHF: 4.5512 };

/** Akcje jednej spolki: ilosc, koszt (z prowizja, srednia wazona) i zysk zrealizowany — jak Position w domenie. */
export interface Holding {
  qty: number;
  cost: number;
  realized: number;
}

export function balancesOf(ledger: DemoLedger): BalanceDto[] {
  const cash: BalanceDto = {
    asset: PLN,
    kind: 'Cash',
    available: toApiAmount(ledger.balance('available', PLN)),
    blocked: toApiAmount(ledger.balance('blocked', PLN)),
  };
  const shares = ledger.symbols().map((s) => ({
    asset: s,
    kind: 'Shares' as const,
    available: String(ledger.balance('available', s)),
    blocked: String(ledger.balance('blocked', s)),
  }));
  return [cash, ...shares];
}

/** Strona historii od najnowszych; kursor to numer ostatniego dokumentu poprzedniej strony. */
export function historyPage(ledger: DemoLedger, limit: number, before: number | null, kind: HistoryKind | null): HistoryPageDto {
  const items = [...ledger.all]
    .reverse()
    .filter((e) => (before === null || e.sequence < before) && (kind === null || KINDS[kind].includes(e.description)))
    .slice(0, limit);
  return { items, nextBefore: items.length === limit ? items[items.length - 1].sequence : null };
}

export function quotesOf(market: DemoMarket): QuoteDto[] {
  return DEMO_SYMBOLS.map((symbol) => {
    const price = market.last(symbol);
    const previous = market.previous(symbol);
    return {
      symbol,
      price: toApiAmount(price),
      previousPrice: previous === null ? null : toApiAmount(previous),
      at: new Date(market.lastAt(symbol)).toISOString(),
      direction: previous === null || previous === price ? 'flat' : price > previous ? 'up' : 'down',
    };
  });
}

export function portfolioOf(holdings: ReadonlyMap<string, Holding>, ledger: DemoLedger, market: DemoMarket, now: number): PortfolioDto {
  const all = [...holdings];
  const positions = all
    .filter(([, h]) => h.qty > 0)
    .map(([symbol, h]) => {
      const last = market.last(symbol);
      const value = h.qty * last;
      const pnl = value - h.cost;
      return {
        symbol,
        quantity: h.qty,
        averageCost: toApiAmount(Math.round(h.cost / h.qty)),
        costBasis: toApiAmount(h.cost),
        lastPrice: toApiAmount(last),
        priceAt: new Date(market.lastAt(symbol)).toISOString(),
        marketValue: toApiAmount(value),
        unrealizedPnl: toApiAmount(pnl),
        returnPercent: h.cost > 0 ? (Math.round((pnl * 10_000) / h.cost) / 100).toFixed(2) : null,
        realizedPnl: toApiAmount(h.realized),
      };
    });
  const closed = all.filter(([, h]) => h.qty === 0 && h.realized !== 0).map(([symbol, h]) => ({ symbol, realizedPnl: toApiAmount(h.realized) }));
  const sum = (f: (h: Holding, symbol: string) => number) => all.reduce((total, [s, h]) => total + f(h, s), 0);
  const cash = ledger.balance('available', PLN) + ledger.balance('blocked', PLN);
  const sharesValue = sum((h, s) => h.qty * market.last(s));
  const cost = sum((h) => (h.qty > 0 ? h.cost : 0));
  const total = cash + sharesValue;
  return {
    positions,
    closed,
    summary: {
      cash: toApiAmount(cash),
      sharesValue: toApiAmount(sharesValue),
      costBasis: toApiAmount(cost),
      unrealizedPnl: toApiAmount(sharesValue - cost),
      realizedPnl: toApiAmount(sum((h) => h.realized)),
      totalValue: toApiAmount(total),
    },
    totalValueIn: Object.entries(NBP).map(([currency, mid]) => ({ currency, amount: toApiAmount(Math.round(total / mid)) })),
    rates: {
      source: 'NBP',
      table: '202/A/NBP/2026',
      effectiveDate: new Date(now).toISOString().slice(0, 10),
      stale: false,
      mid: Object.fromEntries(Object.entries(NBP).map(([c, m]) => [c, m.toFixed(4)])),
    },
  };
}

/** Wynik bez wplat: punkt na koniec kazdego dnia i "teraz" — jak GET /api/me/performance. */
export function performanceOf(ledger: DemoLedger, market: DemoMarket, opened: number, now: number, days: number): PerformancePointDto[] {
  const from = Math.max(opened, now - days * DAY);
  const times: number[] = [];
  for (let t = startOfNextDay(from); t < now; t += DAY) {
    times.push(t - 1);
  }
  times.push(now);
  return times.map((t) => {
    const cash = ledger.balance('available', PLN, t) + ledger.balance('blocked', PLN, t);
    const shares = ledger
      .symbols()
      .reduce((v, s) => v + (ledger.balance('available', s, t) + ledger.balance('blocked', s, t)) * market.priceAt(s, t), 0);
    const invested = ledger.all
      .filter((e) => Date.parse(e.createdAt) <= t && (e.description === 'deposit' || e.description === 'withdrawal'))
      .reduce((sum, e) => sum + (parseCents(e.changes[0].change) ?? 0), 0);
    const value = cash + shares;
    return { at: new Date(t).toISOString(), value: toApiAmount(value), invested: toApiAmount(invested), result: toApiAmount(value - invested) };
  });
}

function startOfNextDay(t: number): number {
  const d = new Date(t);
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1).getTime();
}
