import { HttpErrorResponse, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { delay, Observable, of, share, throwError } from 'rxjs';
import { HistoryKind } from '../core/account/account-api';
import { ClientEvent, StreamStatus } from '../core/events/client-events';
import { parseCents } from '../core/format/money';
import { DemoBroker, DemoRefusal } from './demo-broker';
import { DemoMarket } from './demo-market';

/** Co ile "inni uczestnicy" zawieraja transakcje — rynek sie rusza, czekajace zlecenia moga sie wykonac. */
export const DEMO_TICK_MS = 4_000;
/** Udawane opoznienie sieci, zeby szkielety i przyciski "w toku" bylo widac. */
const LATENCY_MS = 150;

/** Jeden broker na karte przegladarki; odswiezenie strony zaczyna od tej samej historii. */
@Injectable({ providedIn: 'root' })
export class DemoBackend {
  readonly broker = new DemoBroker(new DemoMarket(Date.now() - 19 * 86_400_000, Date.now()), () => Date.now());

  constructor() {
    const timer = setInterval(() => this.broker.tick(), DEMO_TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  handle(req: HttpRequest<unknown>): { status: number; body: unknown } {
    const b = this.broker;
    const path = req.url;
    const body = (req.body ?? {}) as Record<string, unknown>;
    const key = req.headers.get('Idempotency-Key') ?? '';
    const num = (name: string, fallback: number) => Number(req.params.get(name) ?? fallback);
    const order = /^\/api\/me\/orders\/([^/]+)$/.exec(path)?.[1];
    try {
      switch (`${req.method} ${order ? '/api/me/orders/:id' : path}`) {
        case 'POST /api/auth/register':
          return { status: 201, body: {} };
        case 'POST /api/auth/login':
          return { status: 200, body: { accessToken: 'demo', expiresAt: new Date(Date.now() + 8 * 3_600_000).toISOString() } };
        case 'GET /api/me/balances':
          return { status: 200, body: b.balances() };
        case 'GET /api/me/history':
          return { status: 200, body: b.history(num('limit', 20), req.params.has('before') ? num('before', 0) : null, (req.params.get('kind') as HistoryKind | null) ?? null) };
        case 'GET /api/me/portfolio':
          return { status: 200, body: b.portfolio() };
        case 'GET /api/me/performance':
          return { status: 200, body: { points: b.performance(num('days', 30)) } };
        case 'GET /api/market/quotes':
          return { status: 200, body: b.quotes() };
        case 'GET /api/me/orders':
          return { status: 200, body: b.orderList(num('limit', 20)) };
        case 'GET /api/me/orders/:id': {
          const found = b.order(order!);
          return found ? { status: 200, body: found } : { status: 404, body: { title: 'Order not found' } };
        }
        case 'DELETE /api/me/orders/:id':
          return { status: 202, body: b.cancel(order!) };
        case 'POST /api/me/deposits':
          return transfer(b.deposit(parseCents(String(body['amount'])) ?? 0, key));
        case 'POST /api/me/withdrawals':
          return transfer(b.withdraw(parseCents(String(body['amount'])) ?? 0, key));
        case 'POST /api/me/orders': {
          const placed = b.place(
            {
              symbol: String(body['symbol']),
              side: body['side'] === 'sell' ? 'sell' : 'buy',
              type: body['type'] === 'market' ? 'market' : 'limit',
              limitCents: body['limitPrice'] ? parseCents(String(body['limitPrice'])) : null,
              quantity: Number(body['quantity']),
            },
            key,
          );
          return { status: 201, body: placed };
        }
        default:
          return { status: 404, body: { title: 'Not available in demo' } };
      }
    } catch (e) {
      if (e instanceof DemoRefusal) {
        return { status: e.status, body: e.body };
      }
      throw e;
    }
  }
}

function transfer(outcome: 'posted' | 'alreadyPosted') {
  return outcome === 'posted' ? { status: 201, body: { outcome } } : { status: 200, body: { outcome } };
}

/** Zamiast sieci: odpowiedz symulowanego brokera, jako zwykla odpowiedz albo blad HTTP. */
export const demoInterceptor: HttpInterceptorFn = (req) => {
  const { status, body } = inject(DemoBackend).handle(req);
  const result: Observable<never | HttpResponse<unknown>> =
    status < 400 ? of(new HttpResponse({ status, body, url: req.url })) : throwError(() => new HttpErrorResponse({ status, error: body, url: req.url }));
  return result.pipe(delay(LATENCY_MS));
};

/** Strumien zdarzen trybu demo: sygnaly prosto z symulowanego brokera, polaczenie zawsze "Live". */
@Injectable({ providedIn: 'root' })
export class DemoClientEvents {
  private readonly backend = inject(DemoBackend);
  readonly status = signal<StreamStatus>('live').asReadonly();
  readonly lastHeardAt = signal<Date | null>(new Date()).asReadonly();
  readonly nextAttemptAt = signal<number | null>(null).asReadonly();
  readonly events$: Observable<ClientEvent> = this.backend.broker.events.pipe(share());

  reconnect(): void {
    // W demo polaczenie nigdy sie nie zrywa.
  }
}
