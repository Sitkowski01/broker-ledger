import { formatPrice, formatQuantity } from '../format/money';
import { Toast } from '../notifications/toasts';
import { OrderDto } from './order-api';

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
