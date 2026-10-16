import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, filter, groupBy, mergeMap, switchMap } from 'rxjs';
import { ClientEvent, ClientEvents } from '../events/client-events';
import { Toasts } from '../notifications/toasts';
import { OrderApi, OrderDto } from './order-api';
import { orderToast } from './order-toast';

type OrderEvent = Extract<ClientEvent, { kind: 'order' }>;

/**
 * Powiadomienia o tym, co z zleceniem stalo sie na gieldzie: kolejna transakcja, wykonanie, zdjecie, odmowa.
 * Sygnal ze strumienia mowi tylko "zlecenie X sie zmienilo", wiec pobieramy je i porownujemy z tym, co juz
 * widzielismy — inaczej kazde odswiezenie ogloszaloby te sama transakcje od nowa.
 */
@Injectable({ providedIn: 'root' })
export class OrderNotifications {
  private readonly api = inject(OrderApi);
  private readonly events = inject(ClientEvents);
  private readonly toasts = inject(Toasts);
  /** Ile akcji z kazdego zlecenia juz oglosilismy i w jakim stanie bylo. */
  private readonly seen = new Map<string, { filled: number; status: string }>();

  /** Wolane raz, z powloki zalogowanej czesci — konczy sie razem z nia. */
  start(destroyRef: DestroyRef): void {
    this.events.events$
      .pipe(
        filter((e): e is OrderEvent => e.kind === 'order'),
        // Jedno zlecenie naraz (switchMap w grupie): z serii sygnalow liczy sie najnowszy stan.
        groupBy((e) => e.orderId),
        mergeMap((group) => group.pipe(switchMap((e) => this.api.get(e.orderId).pipe(catchError(() => EMPTY))))),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((order) => this.announce(order));
  }

  private announce(order: OrderDto): void {
    const before = this.seen.get(order.id) ?? { filled: 0, status: '' };
    this.seen.set(order.id, { filled: order.filledQuantity, status: order.status });
    const toast = orderToast(order, before.filled, before.status);
    if (toast) {
      this.toasts.show(toast);
    }
  }
}
