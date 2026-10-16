import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, filter, groupBy, mergeMap, switchMap } from 'rxjs';
import { ClientEvent, ClientEvents } from '../events/client-events';
import { formatPrice, formatQuantity } from '../format/money';
import { Toast, Toasts } from '../notifications/toasts';
import { OrderApi, OrderDto } from './order-api';

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

/** Co oglosic o zleceniu wzgledem tego, co juz bylo oglaszane; null, gdy nic nowego. */
export function orderToast(o: OrderDto, filledBefore: number, statusBefore: string): Omit<Toast, 'id'> | null {
  const side = o.side === 'buy' ? 'kupno' : 'sprzedaż';
  const link = { path: '/zlecenia', label: 'Przebieg' };
  if (o.status === statusBefore && o.filledQuantity === filledBefore) {
    return null;
  }
  switch (o.status) {
    case 'filled':
      return {
        tone: 'ok',
        title: 'Zlecenie wykonane',
        lines: [`${o.symbol} · ${side} ${formatQuantity(o.quantity)} szt.`, `Wartość ${formatPrice(o.filledValue)} zł · prowizja ${formatPrice(o.commission)} zł`],
        link,
      };
    case 'rejected':
      return { tone: 'bad', title: 'Zlecenie odrzucone', lines: [`${o.symbol} · ${side} ${formatQuantity(o.quantity)} szt.`, 'Blokada wróciła na rachunek'], link };
    case 'cancelled':
      return {
        tone: 'info',
        title: 'Zlecenie zdjęte z giełdy',
        lines: [`${o.symbol} · wykonano ${formatQuantity(o.filledQuantity)} z ${formatQuantity(o.quantity)} szt.`, 'Reszta blokady wraca na rachunek'],
        link,
      };
  }
  if (o.filledQuantity > filledBefore) {
    return {
      tone: 'info',
      title: 'Rozliczono transakcję',
      lines: [`${o.symbol} · ${formatQuantity(o.filledQuantity - filledBefore)} szt. · zostało ${formatQuantity(o.quantity - o.filledQuantity)} szt.`],
      link: { path: '/ksiega', label: 'Księga' },
    };
  }
  return null;
}
