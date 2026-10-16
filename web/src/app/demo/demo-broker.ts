import { Subject } from 'rxjs';
import { BalanceDto, HistoryKind, HistoryPageDto, PerformancePointDto, PortfolioDto } from '../core/account/account-api';
import { ClientEvent } from '../core/events/client-events';
import { parseCents, toApiAmount } from '../core/format/money';
import { QuoteDto } from '../core/market/quote-feed';
import { NewOrder, OrderDto, OrderStatus } from '../core/orders/order-api';
import { buyBlockCents } from '../features/orders/ticket-model';
import { DemoLedger } from './demo-ledger';
import { DemoMarket } from './demo-market';
import { balancesOf, DAY, historyPage, Holding, performanceOf, portfolioOf, quotesOf } from './demo-views';

const PLN = 'PLN';

type Mutable<T> = { -readonly [K in keyof T]: T[K] };

/** Odmowa operacji jak w API: status HTTP i tresc odpowiedzi. */
export class DemoRefusal {
  constructor(
    readonly status: number,
    readonly body: unknown,
  ) {}
}

/**
 * Broker trybu demo w przegladarce. Ta sama droga zlecenia co na serwerze: blokada → gielda → transakcje →
 * zwrot reszty blokady, z prowizja od calego wykonania. "Gielda" wykonuje zlecenie z limitem, gdy cena
 * na rynku go dogoni; rynek co kilka sekund sie rusza. Sygnaly zmian ida tym samym strumieniem co z SSE.
 */
export class DemoBroker {
  readonly events = new Subject<ClientEvent>();
  readonly opened: number;

  private readonly ledger = new DemoLedger();
  private readonly orders: Mutable<OrderDto>[] = [];
  private readonly keys = new Map<string, string>();
  /** Koszt (z prowizja) i zysk zrealizowany kazdej spolki — srednia wazona, jak Portfolio w domenie. */
  private readonly holdings = new Map<string, Holding>();
  private exchangeOrderId = 100;

  constructor(
    readonly market: DemoMarket,
    private readonly now: () => number,
    private readonly schedule: (fn: () => void, ms: number) => void = (fn, ms) => setTimeout(fn, ms),
  ) {
    // Rachunek otwarty 18 dni temu o 9:00 — historia ma ludzkie godziny niezaleznie od chwili otwarcia demo.
    const start = new Date(now() - 18 * DAY);
    this.opened = new Date(start.getFullYear(), start.getMonth(), start.getDate(), 9).getTime();
  }

  // ---- zapytania (ksztalt odpowiedzi API, patrz demo-views) ----

  balances(): BalanceDto[] {
    return balancesOf(this.ledger);
  }

  history(limit: number, before: number | null, kind: HistoryKind | null): HistoryPageDto {
    return historyPage(this.ledger, limit, before, kind);
  }

  orderList(limit: number): OrderDto[] {
    return this.orders.slice(0, limit).map((o) => ({ ...o }));
  }

  order(id: string): OrderDto | null {
    const o = this.orders.find((x) => x.id === id);
    return o ? { ...o } : null;
  }

  quotes(): QuoteDto[] {
    return quotesOf(this.market);
  }

  portfolio(): PortfolioDto {
    return portfolioOf(this.holdings, this.ledger, this.market, this.now());
  }

  performance(days: number): PerformancePointDto[] {
    return performanceOf(this.ledger, this.market, this.opened, this.now(), days);
  }

  // ---- operacje ----

  deposit(cents: number, key: string): 'posted' | 'alreadyPosted' {
    if (this.keys.has(`dep:${key}`)) {
      return 'alreadyPosted';
    }
    this.keys.set(`dep:${key}`, key);
    this.ledger.deposit(cents, this.now(), key);
    this.events.next({ kind: 'ledger' });
    return 'posted';
  }

  withdraw(cents: number, key: string): 'posted' | 'alreadyPosted' {
    if (this.keys.has(`wd:${key}`)) {
      return 'alreadyPosted';
    }
    if (cents > this.cash('available')) {
      throw new DemoRefusal(422, { title: 'Insufficient funds', asset: PLN, available: toApiAmount(this.cash('available')) });
    }
    this.keys.set(`wd:${key}`, key);
    this.ledger.withdraw(cents, this.now(), key);
    this.events.next({ kind: 'ledger' });
    return 'posted';
  }

  /** Zlecenie jak POST /api/me/orders: ta sama prosba z tym samym kluczem zwraca to samo zlecenie. */
  place(request: NewOrder, key: string): OrderDto {
    const existing = this.keys.get(`order:${key}`);
    if (existing) {
      return this.order(existing)!;
    }
    const at = this.now();
    const order = this.newOrder(request, at);
    this.keys.set(`order:${key}`, order.id);
    this.orders.unshift(order);
    this.signal(order);
    const asset = request.side === 'buy' ? PLN : request.symbol;
    const blockUnits = asset === PLN ? (parseCents(order.blocked) ?? 0) : Number(order.blocked);
    if (this.ledger.balance('available', asset) < blockUnits) {
      order.status = 'rejected';
      order.rejectionReason = 'insufficient funds';
      this.signal(order);
      throw new DemoRefusal(422, { ...order });
    }
    this.ledger.block(asset, blockUnits, at, order.id);
    order.fundsBlocked = true;
    order.status = 'accepted';
    order.exchangeOrderId = ++this.exchangeOrderId;
    this.events.next({ kind: 'ledger' });
    this.signal(order);
    return { ...order };
  }

  // ---- wnetrze ----

  private cash(account: 'available' | 'blocked'): number {
    return this.ledger.balance(account, PLN);
  }

  private newOrder(request: NewOrder, at: number): Mutable<OrderDto> {
    const blocked = request.side === 'buy' ? toApiAmount(buyBlockCents(request.limitCents!, request.quantity)) : String(request.quantity);
    return {
      id: demoId(at, this.orders.length + 1),
      symbol: request.symbol,
      side: request.side,
      type: request.type,
      limitPrice: request.limitCents === null ? null : toApiAmount(request.limitCents),
      quantity: request.quantity,
      status: 'pending' as OrderStatus,
      exchangeOrderId: null,
      rejectionReason: null,
      blockedAsset: request.side === 'buy' ? PLN : request.symbol,
      blocked,
      filledQuantity: 0,
      filledValue: '0.00',
      commission: '0.00',
      createdAt: new Date(at).toISOString(),
      fundsBlocked: false,
      fundsReleased: false,
    };
  }

  private signal(order: OrderDto): void {
    this.events.next({ kind: 'order', orderId: order.id, status: order.status });
  }

}

/** Identyfikator w ksztalcie UUID; koniec (krotki numer na ekranie) rozni sie miedzy zleceniami. */
function demoId(at: number, n: number): string {
  const hex = (v: number, len: number) => (v >>> 0).toString(16).padStart(len, '0').slice(-len);
  const mix = Math.imul(n, 2_654_435_761) ^ at;
  return `${hex(at / 65_536, 8)}-${hex(at, 4)}-7000-8000-${hex(n, 4)}${hex(mix, 8)}`;
}
