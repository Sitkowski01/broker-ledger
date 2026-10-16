import { orderToast } from './order-toast';
import { OrderDto } from './order-api';

const order = (o: Partial<OrderDto>): OrderDto => ({
  id: 'o', symbol: 'CDR', side: 'buy', type: 'limit', limitPrice: '250.00', quantity: 10, status: 'accepted', exchangeOrderId: 7,
  rejectionReason: null, blockedAsset: 'PLN', blocked: '2509.75', filledQuantity: 0, filledValue: '0.00', commission: '0.00',
  createdAt: null, fundsBlocked: true, fundsReleased: false, ...o,
});
const plain = (s: unknown) => JSON.stringify(s).replace(/ /g, ' ');

describe('order toasts', () => {
  it('announces a new trade, the fill, a cancellation and a rejection — each once', () => {
    expect(plain(orderToast(order({ filledQuantity: 4 }), 0, 'accepted'))).toContain('"title":"Rozliczono transakcję","lines":["CDR · 4 szt. · zostało 6 szt."]');
    expect(orderToast(order({ filledQuantity: 4 }), 4, 'accepted')).toBeNull();
    expect(plain(orderToast(order({ status: 'filled', filledQuantity: 10, filledValue: '2480.00', commission: '9.68' }), 4, 'accepted'))).toContain(
      'Wartość 2 480,00 zł · prowizja 9,68 zł',
    );
    expect(orderToast(order({ status: 'cancelled', filledQuantity: 3 }), 3, 'cancelrequested')?.title).toBe('Zlecenie zdjęte z giełdy');
    expect(orderToast(order({ status: 'rejected', side: 'sell' }), 0, 'pending')).toMatchObject({ tone: 'bad', title: 'Zlecenie odrzucone' });
    // Przyjecie przez gielde widac w formularzu — bez powiadomienia.
    expect(orderToast(order({}), 0, 'pending')).toBeNull();
  });
});
