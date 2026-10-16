import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { toApiAmount } from '../format/money';

export type OrderSide = 'buy' | 'sell';
export type OrderType = 'limit' | 'market';
export type OrderStatus = 'pending' | 'unknown' | 'accepted' | 'cancelrequested' | 'rejected' | 'filled' | 'cancelled';

/** Zlecenie z API (OrderView). Kwoty jako tekst, jak wszedzie w API. */
export interface OrderDto {
  readonly id: string;
  readonly symbol: string;
  readonly side: OrderSide;
  readonly type: OrderType;
  readonly limitPrice: string | null;
  readonly quantity: number;
  readonly status: OrderStatus;
  readonly exchangeOrderId: number | null;
  readonly rejectionReason: string | null;
  /** "PLN" przy kupnie, symbol spolki przy sprzedazy. */
  readonly blockedAsset: string;
  readonly blocked: string;
  readonly filledQuantity: number;
  readonly filledValue: string;
  readonly commission: string;
  readonly createdAt: string | null;
  readonly fundsBlocked: boolean;
  readonly fundsReleased: boolean;
}

export interface NewOrder {
  readonly symbol: string;
  readonly side: OrderSide;
  readonly type: OrderType;
  /** Limit w groszach; null dla PKC. */
  readonly limitCents: number | null;
  readonly quantity: number;
}

export const ORDERS_URL = '/api/me/orders';

/**
 * Skladanie i anulowanie zlecen. Klucz idempotencji wybiera wolajacy: ponowienie tego samego zlecenia
 * po zerwanym polaczeniu zwraca to samo zlecenie, zamiast blokowac pieniadze drugi raz.
 */
@Injectable({ providedIn: 'root' })
export class OrderApi {
  private readonly http = inject(HttpClient);

  place(order: NewOrder, idempotencyKey: string): Observable<OrderDto> {
    return this.http.post<OrderDto>(
      ORDERS_URL,
      {
        symbol: order.symbol,
        side: order.side,
        type: order.type,
        limitPrice: order.limitCents === null ? null : toApiAmount(order.limitCents),
        quantity: order.quantity,
      },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }

  get(id: string): Observable<OrderDto> {
    return this.http.get<OrderDto>(`${ORDERS_URL}/${id}`);
  }

  cancel(id: string): Observable<OrderDto> {
    return this.http.delete<OrderDto>(`${ORDERS_URL}/${id}`);
  }
}
