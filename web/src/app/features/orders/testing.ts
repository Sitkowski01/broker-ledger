import { OrderDto } from '../../core/orders/order-api';

/** Kupno 10 CDR po 250 zl przyjete przez gielde: blokada 2500 + 9,75 prowizji (jak w testach e2e API). */
export function anOrder(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    id: '01920000-0000-7000-8000-000000000001',
    symbol: 'CDR',
    side: 'buy',
    type: 'limit',
    limitPrice: '250.00',
    quantity: 10,
    status: 'accepted',
    exchangeOrderId: 7,
    rejectionReason: null,
    blockedAsset: 'PLN',
    blocked: '2509.75',
    filledQuantity: 0,
    filledValue: '0.00',
    commission: '0.00',
    createdAt: '2026-10-16T17:43:10Z',
    fundsBlocked: true,
    fundsReleased: false,
    ...overrides,
  };
}

/** Twarde spacje z formatowania kwot zamienione na zwykle — czytelniejsze oczekiwania. */
export function plainDeep<T>(value: T): T {
  return JSON.parse(JSON.stringify(value).split(String.fromCharCode(0xa0)).join(' ')) as T;
}
