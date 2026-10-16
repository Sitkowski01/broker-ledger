import { OrderDto } from './order-api';
import { orderToast } from './order-toast';

const order = (o: Partial<OrderDto>): OrderDto => ({
  id: 'o', symbol: 'CDR', side: 'buy', type: 'limit', limitPrice: '250.00', quantity: 10, status: 'accepted', exchangeOrderId: 7,
  rejectionReason: null, blockedAsset: 'PLN', blocked: '2509.75', filledQuantity: 0, filledValue: '0.00', commission: '0.00',
  createdAt: null, fundsBlocked: true, fundsReleased: false, ...o,
});
/** Twarde spacje z formatowania kwot na zwykle — czytelniejsze oczekiwania. */
const plain = <T>(value: T): T => JSON.parse(JSON.stringify(value).split(String.fromCharCode(0xa0)).join(' ')) as T;
const toOrders = { path: '/zlecenia', label: 'Przebieg' };

describe('order toasts', () => {
  it('announces each new trade with what is left', () => {
    expect(orderToast(order({ filledQuantity: 4 }), 0, 'accepted')).toEqual({
      tone: 'info',
      title: 'Rozliczono transakcję',
      lines: ['CDR · 4 szt. · zostało 6 szt.'],
      link: { path: '/ksiega', label: 'Księga' },
    });
    expect(orderToast(order({ filledQuantity: 7 }), 4, 'accepted')?.lines).toEqual(['CDR · 3 szt. · zostało 3 szt.']);
    // To samo, co juz ogloszone, nie wraca drugi raz.
    expect(orderToast(order({ filledQuantity: 4 }), 4, 'accepted')).toBeNull();
  });

  it('announces the fill with value and commission', () => {
    expect(plain(orderToast(order({ status: 'filled', filledQuantity: 1000, quantity: 1000, filledValue: '2480.00', commission: '9.68' }), 4, 'accepted'))).toEqual({
      tone: 'ok',
      title: 'Zlecenie wykonane',
      lines: ['CDR · kupno 1 000 szt.', 'Wartość 2 480,00 zł · prowizja 9,68 zł'],
      link: toOrders,
    });
    expect(orderToast(order({ status: 'filled', side: 'sell', filledQuantity: 10 }), 0, 'accepted')?.lines[0]).toBe('CDR · sprzedaż 10 szt.');
  });

  it('announces a cancellation and a rejection', () => {
    expect(orderToast(order({ status: 'cancelled', filledQuantity: 3 }), 3, 'cancelrequested')).toEqual({
      tone: 'info',
      title: 'Zlecenie zdjęte z giełdy',
      lines: ['CDR · wykonano 3 z 10 szt.', 'Reszta blokady wraca na rachunek'],
      link: toOrders,
    });
    expect(orderToast(order({ status: 'rejected', side: 'sell' }), 0, 'pending')).toEqual({
      tone: 'bad',
      title: 'Zlecenie odrzucone',
      lines: ['CDR · sprzedaż 10 szt.', 'Blokada wróciła na rachunek'],
      link: toOrders,
    });
  });

  it('stays quiet when nothing changed and when the exchange only accepted the order', () => {
    expect(orderToast(order({ status: 'filled', filledQuantity: 10 }), 10, 'filled')).toBeNull();
    // Przyjecie przez gielde widac w formularzu — bez powiadomienia.
    expect(orderToast(order({}), 0, 'pending')).toBeNull();
  });
});
