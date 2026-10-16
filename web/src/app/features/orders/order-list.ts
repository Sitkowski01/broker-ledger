import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { toApiError } from '../../core/api/api-error';
import { shortDateTime } from '../../core/format/dates';
import { OrderApi, OrderDto } from '../../core/orders/order-api';
import { matchesFilter, OrderFilter, orderRow } from './order-model';
import { OrderSaga } from './order-saga';

const FILTERS: readonly { key: OrderFilter; label: string }[] = [
  { key: 'all', label: 'Wszystkie' },
  { key: 'active', label: 'Aktywne' },
  { key: 'done', label: 'Zakończone' },
];

/**
 * Moje zlecenia: filtr, anulowanie z potwierdzeniem i przebieg wybranego zlecenia.
 * Anulowanie tylko wysyla prosbe — reszte blokady zwolni zdarzenie z gieldy, wiec po nim odswiezamy liste.
 */
@Component({
  selector: 'bl-order-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [OrderSaga],
  templateUrl: './order-list.html',
  host: { class: 'panel', role: 'region', 'aria-labelledby': 'orders-title' },
})
export class OrderList {
  private readonly api = inject(OrderApi);

  readonly orders = input.required<readonly OrderDto[]>();
  readonly now = input.required<Date>();
  /** Zlecenie zmienilo stan (anulowanie) — rodzic odswieza liste i salda. */
  readonly changed = output<void>();

  protected readonly filter = signal<OrderFilter>('all');
  private readonly chosen = signal<string | null>(null);
  protected readonly asking = signal<string | null>(null);
  protected readonly cancelling = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly filters = computed(() =>
    FILTERS.map((f) => ({ ...f, count: this.orders().filter((o) => matchesFilter(o, f.key)).length })),
  );

  protected readonly rows = computed(() => {
    const now = this.now();
    return this.orders()
      .filter((o) => matchesFilter(o, this.filter()))
      .map((o) => orderRow(o, (iso) => shortDateTime(iso, now)));
  });

  /** Wybrane zlecenie; bez wyboru — najnowsze, zeby os przebiegu nigdy nie byla pusta. */
  protected readonly selected = computed<OrderDto | null>(() => {
    const orders = this.orders();
    return orders.find((o) => o.id === this.chosen()) ?? (orders.length ? orders[0] : null);
  });

  protected select(id: string): void {
    this.chosen.set(id);
  }

  protected ask(id: string): void {
    this.error.set(null);
    this.asking.set(id);
  }

  protected dismiss(): void {
    this.asking.set(null);
  }

  protected confirm(id: string): void {
    this.asking.set(null);
    this.cancelling.set(id);
    this.api.cancel(id).subscribe({
      next: () => {
        this.cancelling.set(null);
        this.chosen.set(id);
        this.changed.emit();
      },
      error: (e: unknown) => {
        this.cancelling.set(null);
        const api = toApiError(e);
        // 409: zlecenie zdazylo sie wykonac albo juz je zdjeto — lista pokaze aktualny stan.
        this.error.set(
          api.kind === 'conflict'
            ? 'Tego zlecenia nie da się już anulować — zdążyło się zakończyć.'
            : api.kind === 'server'
              ? 'Giełda nie odpowiedziała na anulowanie. Spróbuj ponownie za chwilę.'
              : 'message' in api ? api.message : 'Nie udało się anulować zlecenia.',
        );
        this.changed.emit();
      },
    });
  }
}
