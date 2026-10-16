import { parseCents } from '../core/format/money';
import { ClientEvent } from '../core/events/client-events';
import { DemoBroker, DemoRefusal } from './demo-broker';
import { DemoMarket } from './demo-market';

const NOW = new Date('2026-10-16T18:00:00Z').getTime();

describe('DemoBroker', () => {
  function setup() {
    let now = NOW;
    const queue: (() => void)[] = [];
    const broker = new DemoBroker(new DemoMarket(NOW - 19 * 86_400_000, NOW), () => now, (fn) => queue.push(fn));
    const events: ClientEvent[] = [];
    broker.events.subscribe((e) => events.push(e));
    /** Wykonuje wszystko, co "gielda" zaplanowala (takze to, co zaplanuja kolejne kroki). */
    const settle = () => {
      while (queue.length) {
        now += 1_000;
        queue.shift()!();
      }
    };
    const cash = () => broker.balances().find((b) => b.asset === 'PLN')!;
    const cents = (s: string) => parseCents(s) ?? 0;
    return { broker, events, settle, cash, cents };
  }

  it('starts with a consistent history: value equals cash plus shares, the result excludes the deposit', () => {
    const { broker, cents } = setup();
    const p = broker.portfolio();
    expect(cents(p.summary.totalValue)).toBe(cents(p.summary.cash) + cents(p.summary.sharesValue));
    expect(p.positions.map((x) => x.symbol)).toEqual(['CDR', 'PKN', 'ALE', 'KGH']);
    const last = broker.performance(30).at(-1)!;
    expect(cents(last.value)).toBe(cents(p.summary.totalValue));
    expect(last.invested).toBe('25000.00');
    expect(broker.history(100, null, 'transfers').items).toHaveLength(1);
    // Wszystko zostalo rozliczone: nic nie wisi pod zleceniami.
    expect(broker.balances().every((b) => Number(b.blocked) === 0)).toBe(true);
  });

  it('books a deposit once per key and refuses to withdraw more than is free', () => {
    const { broker, events, cash, cents } = setup();
    const before = cents(cash().available);
    expect(broker.deposit(100_000, 'k')).toBe('posted');
    expect(broker.deposit(100_000, 'k')).toBe('alreadyPosted');
    expect(cents(cash().available)).toBe(before + 100_000);
    expect(events).toEqual([{ kind: 'ledger' }]);
    expect(() => broker.withdraw(before + 100_001, 'w')).toThrow(DemoRefusal);
    broker.withdraw(1_000, 'w');
    expect(cents(cash().available)).toBe(before + 99_000);
  });

  it('runs a buy through the saga and keeps every grosz: block, two trades, the rest returned', () => {
    const { broker, events, settle, cash, cents } = setup();
    const free = cents(cash().available);
    const price = broker.market.last('PZU');

    const placed = broker.place({ symbol: 'PZU', side: 'buy', type: 'limit', limitCents: price + 100, quantity: 10 }, 'o1');
    expect([placed.status, placed.fundsBlocked, cents(cash().blocked)]).toEqual(['accepted', true, cents(placed.blocked)]);
    expect(broker.place({ symbol: 'PZU', side: 'buy', type: 'limit', limitCents: price + 100, quantity: 10 }, 'o1').id).toBe(placed.id);

    settle();
    const done = broker.order(placed.id)!;
    expect([done.status, done.filledQuantity, done.fundsReleased]).toEqual(['filled', 10, true]);
    // Zaplacono wartosc + prowizja od calego wykonania; reszta blokady wrocila.
    expect(cents(cash().available)).toBe(free - cents(done.filledValue) - cents(done.commission));
    expect(cents(cash().blocked)).toBe(0);
    expect(broker.history(10, null, 'trades').items.filter((e) => e.orderId === placed.id)).toHaveLength(2);
    expect(events.filter((e) => e.kind === 'order').map((e) => (e.kind === 'order' ? e.status : ''))).toEqual(['pending', 'accepted', 'accepted', 'filled']);
  });

  it('keeps a limit order waiting until the market reaches it, and cancels the rest', () => {
    const { broker, settle } = setup();
    const placed = broker.place({ symbol: 'PKO', side: 'buy', type: 'limit', limitCents: 1, quantity: 3 }, 'cheap');
    settle();
    expect(broker.order(placed.id)!.status).toBe('accepted');

    broker.cancel(placed.id);
    settle();
    expect([broker.order(placed.id)!.status, broker.order(placed.id)!.fundsReleased]).toEqual(['cancelled', true]);
    expect(() => broker.cancel(placed.id)).toThrow(DemoRefusal);
    expect(() => broker.cancel('nope')).toThrow(DemoRefusal);
  });

  it('rejects an order the client cannot cover, and sells shares at market', () => {
    const { broker, settle, cents } = setup();
    expect(() => broker.place({ symbol: 'KGH', side: 'buy', type: 'limit', limitCents: 10_000_000, quantity: 1_000 }, 'big')).toThrow(DemoRefusal);
    expect(broker.orderList(1)[0].status).toBe('rejected');

    const realizedBefore = cents(broker.portfolio().summary.realizedPnl);
    const sold = broker.place({ symbol: 'ALE', side: 'sell', type: 'market', limitCents: null, quantity: 100 }, 's');
    settle();
    expect(broker.order(sold.id)!.status).toBe('filled');
    expect(broker.portfolio().positions.map((p) => p.symbol)).not.toContain('ALE');
    expect(broker.portfolio().closed.map((c) => c.symbol)).toContain('ALE');
    expect(cents(broker.portfolio().summary.realizedPnl)).not.toBe(realizedBefore);
  });

  it('pages the history with the cursor like the API', () => {
    const { broker } = setup();
    const first = broker.history(3, null, null);
    const second = broker.history(3, first.nextBefore, null);
    expect(first.items[0].sequence).toBeGreaterThan(second.items[0].sequence);
    expect(second.items[0].sequence).toBe(first.items[2].sequence - 1);
    expect(broker.history(1000, null, null).nextBefore).toBeNull();
  });

  it('moves the market and gives quotes with a direction', () => {
    const { broker } = setup();
    broker.tick();
    const q = broker.quotes();
    expect(q.map((x) => x.symbol)).toEqual(['ALE', 'CDR', 'KGH', 'PKN', 'PKO', 'PZU']);
    expect(q.every((x) => ['up', 'down', 'flat'].includes(x.direction) && x.previousPrice !== null)).toBe(true);
  });
});
