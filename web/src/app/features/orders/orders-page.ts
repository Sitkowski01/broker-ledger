import { DOCUMENT } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ACCOUNT_URLS, BalanceDto } from '../../core/account/account-api';
import { cashOf, sharesOf } from '../../core/account/balances';
import { toApiError } from '../../core/api/api-error';
import { lastGood, loadErrorOf } from '../../core/api/last-good';
import { QuoteFeed } from '../../core/market/quote-feed';
import { OrderDto, ORDERS_URL } from '../../core/orders/order-api';
import { PageHeader } from '../../shared/page-header';
import { refreshDelay } from './order-model';
import { OrderList } from './order-list';
import { OrderTicket } from './order-ticket';

/** Ile ostatnich zlecen pokazujemy (API: najwyzej 100). */
const ORDERS_WINDOW = 50;
/** Co ile sprawdzamy, czy pora odswiezyc liste (tempo wybiera refreshDelay). */
export const TICK_MS = 1_000;

/** Zlecenia: formularz z blokada liczona przed wyslaniem, lista z anulowaniem i przebieg sagi. */
@Component({
  selector: 'bl-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, OrderTicket, OrderList],
  template: `
    <bl-page-header kicker="Handel · sesja ciągła" title="Zlecenia" />

    @if (loadError(); as message) {
      <div class="alert error" role="alert">
        <span>{{ message }}</span>
        <button type="button" class="ghost" (click)="reload()">Spróbuj ponownie</button>
      </div>
    }

    @if (ready()) {
      <div class="row">
        <bl-order-ticket class="ticket" [available]="cash().available" [holdings]="holdings()" [prices]="prices()" (placed)="reload()" />
        <bl-order-list class="list" [orders]="orders()!" [now]="now()" (changed)="reload()" />
      </div>
    } @else if (!loadError()) {
      <section class="panel loading" aria-busy="true" aria-label="Wczytywanie zleceń">
        <div class="bar"><h2>Moje zlecenia</h2><span class="meta">Wczytywanie…</span></div>
      </section>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 24px;
      align-items: flex-start;
    }
    .ticket {
      flex: 1 1 min(360px, 100%);
      max-width: 440px;
      min-width: 0;
    }
    .list {
      flex: 999 1 min(640px, 100%);
      min-width: 0;
    }
    .alert.error {
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
    }
    .loading {
      min-height: 240px;
    }
  `,
})
export class OrdersPage {
  private readonly quotes = inject(QuoteFeed);

  protected readonly now = signal(new Date());

  private readonly ordersResource = httpResource<OrderDto[]>(() => ({ url: ORDERS_URL, params: { limit: ORDERS_WINDOW } }));
  private readonly balancesResource = httpResource<BalanceDto[]>(() => ACCOUNT_URLS.balances);
  protected readonly orders = lastGood(this.ordersResource);
  private readonly balances = lastGood(this.balancesResource);

  protected readonly ready = computed(() => this.orders() !== undefined && this.balances() !== undefined);
  protected readonly cash = computed(() => cashOf(this.balances() ?? []));
  /** Wolne akcje kazdej spolki — tyle mozna sprzedac. */
  protected readonly holdings = computed(() => sharesOf(this.balances() ?? []));
  protected readonly prices = this.quotes.prices;
  private readonly delay = computed(() => refreshDelay(this.orders() ?? []));
  private lastReload = Date.now();

  protected readonly loadError = computed(() => {
    const error = this.ordersResource.error() ?? this.balancesResource.error();
    if (!error) {
      return null;
    }
    const api = toApiError(loadErrorOf(error));
    return 'message' in api ? api.message : 'Nie udało się wczytać zleceń.';
  });

  constructor() {
    // Zlecenia w toku zmieniaja stan bez udzialu klienta (reconciler, transakcje z Kafki) — odswiezamy je,
    // dopoki jakies zyje: czesto przy stanach przejsciowych, rzadko przy zleceniu czekajacym na gieldzie.
    // Ukryta karta nie pyta wcale.
    const document = inject(DOCUMENT);
    const timer = setInterval(() => {
      const now = new Date();
      if (now.getMinutes() !== this.now().getMinutes()) {
        this.now.set(now);
      }
      const delay = this.delay();
      const due = delay !== null && Date.now() - this.lastReload >= delay;
      if (due && document.visibilityState !== 'hidden' && !this.ordersResource.isLoading()) {
        this.reload();
      }
    }, TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected reload(): void {
    this.lastReload = Date.now();
    this.ordersResource.reload();
    this.balancesResource.reload();
  }
}
