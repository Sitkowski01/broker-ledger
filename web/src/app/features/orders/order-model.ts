import { formatPrice, formatQuantity } from '../../core/format/money';
import { OrderDto, OrderSide, OrderStatus } from '../../core/orders/order-api';

/** Stan zlecenia dla ludzi: status, przebieg sagi, wiersz listy i tempo odswiezania. */

export type Tone = 'ok' | 'live' | 'wait' | 'bad' | 'off';

const STATUS: Readonly<Record<OrderStatus, { label: string; tone: Tone }>> = {
  pending: { label: 'Wysyłane', tone: 'off' },
  unknown: { label: 'Nieznany', tone: 'wait' },
  accepted: { label: 'Przyjęte', tone: 'live' },
  cancelrequested: { label: 'Anulowanie', tone: 'wait' },
  rejected: { label: 'Odrzucone', tone: 'bad' },
  filled: { label: 'Wykonane', tone: 'ok' },
  cancelled: { label: 'Anulowane', tone: 'off' },
};

export function statusOf(order: Pick<OrderDto, 'status' | 'filledQuantity'>): { label: string; tone: Tone } {
  if (order.status === 'accepted' && order.filledQuantity > 0) {
    return { label: 'Częściowo', tone: 'live' };
  }
  return STATUS[order.status] ?? { label: order.status, tone: 'off' };
}

/** Zlecenie jeszcze zyje: czeka na gieldzie albo na wyjasnienie. */
export function isActive(order: Pick<OrderDto, 'status'>): boolean {
  return order.status === 'pending' || order.status === 'unknown' || order.status === 'accepted' || order.status === 'cancelrequested';
}

/** Anulowac mozna tylko zlecenie, ktore gielda przyjela (OrderSaga.CancelAsync). */
export function canCancel(order: Pick<OrderDto, 'status'>): boolean {
  return order.status === 'accepted';
}

/** Powody odmowy z domeny i gieldy po polsku; nieznany zostaje, jak przyszedl. */
export function rejectionText(reason: string | null): string | null {
  if (reason === null) {
    return null;
  }
  if (reason === 'insufficient funds') {
    return 'Za mało wolnych środków albo akcji — nic nie zablokowano.';
  }
  return `Giełda odrzuciła zlecenie: ${reason}.`;
}

/** Co stoi pod zleceniem: zlotowki przy kupnie, akcje przy sprzedazy. */
export function blockedText(o: Pick<OrderDto, 'blockedAsset' | 'blocked'>): string {
  return o.blockedAsset === 'PLN' ? `${formatPrice(o.blocked)} zł` : `${formatQuantity(Number(o.blocked) || 0)} szt.`;
}

export interface SagaStep {
  readonly name: string;
  readonly note: string;
  readonly state: 'done' | 'now' | 'todo' | 'fail';
}

/**
 * Przebieg sagi z jednego zlecenia: zapis → blokada → gielda → rozliczenie → zwolnienie reszty.
 * Kazdy krok wynika z pol zlecenia, wiec os pokazuje to samo, co wie broker — nic nie zgaduje.
 */
export function sagaSteps(o: OrderDto, time: (iso: string) => string): SagaStep[] {
  const blocked = blockedText(o);
  const finished = o.status === 'rejected' || o.status === 'filled' || o.status === 'cancelled';
  const neverBlocked = o.status === 'rejected' && !o.fundsBlocked;

  const saved: SagaStep = { name: 'Zapisane', note: o.createdAt ? time(o.createdAt) : '—', state: 'done' };
  const block: SagaStep = neverBlocked
    ? { name: 'Blokada', note: 'za mało środków', state: 'fail' }
    : o.fundsBlocked
      ? { name: 'Blokada', note: blocked, state: 'done' }
      : { name: 'Blokada', note: 'w toku', state: 'now' };

  let exchange: SagaStep;
  if (o.exchangeOrderId !== null) {
    exchange = { name: 'Przyjęte', note: `nr giełdy ${o.exchangeOrderId}`, state: 'done' };
  } else if (o.status === 'unknown') {
    exchange = { name: 'Wyjaśniane', note: 'reconciler pyta giełdę', state: 'now' };
  } else if (o.status === 'rejected' && o.fundsBlocked) {
    exchange = { name: 'Giełda', note: 'odrzuciła', state: 'fail' };
  } else {
    exchange = { name: 'Giełda', note: o.fundsBlocked && !finished ? 'wysyłanie' : '—', state: o.fundsBlocked && !finished ? 'now' : 'todo' };
  }

  const fill = `${formatQuantity(o.filledQuantity)} / ${formatQuantity(o.quantity)} szt.`;
  const settled: SagaStep =
    o.filledQuantity >= o.quantity
      ? { name: 'Rozliczone', note: fill, state: 'done' }
      : o.exchangeOrderId !== null && !finished
        ? { name: 'Rozliczane', note: fill, state: 'now' }
        : { name: 'Rozliczone', note: o.filledQuantity > 0 ? fill : '—', state: o.filledQuantity > 0 ? 'done' : 'todo' };

  const release: SagaStep = o.fundsReleased
    ? { name: 'Zwolnione', note: 'reszta blokady wróciła', state: 'done' }
    : finished && o.fundsBlocked
      ? { name: 'Zwalnianie', note: 'w toku', state: 'now' }
      : { name: 'Zwolnione', note: '—', state: 'todo' };

  return [saved, block, exchange, settled, release];
}

/** Jedno zdanie pod osia: co sie dzieje i co z pieniedzmi. Pusty tekst, gdy nie ma czego dopowiadac. */
export function sagaNote(o: OrderDto): string {
  switch (o.status) {
    case 'unknown':
      return 'Giełda nie odpowiedziała na czas, więc nie wiadomo, czy zlecenie weszło. Reconciler zapyta ją po kluczu zlecenia — środki zostają zablokowane, dopóki stan się nie wyjaśni.';
    case 'pending':
      return 'Zlecenie jest w drodze na giełdę. Jeśli odpowiedź nie przyjdzie, dokończy je reconciler w tle.';
    case 'cancelrequested':
      return 'Giełda przyjęła anulowanie. Resztę blokady zwolni jej zdarzenie z Kafki — dopiero ono mówi, ile zdążyło się wykonać.';
    case 'rejected':
      return rejectionText(o.rejectionReason) ?? '';
    case 'cancelled':
      return o.filledQuantity > 0 ? 'Giełda zdjęła resztę zlecenia — niewykonana część blokady wróciła na rachunek.' : 'Zlecenie zdjęte z giełdy, blokada wróciła na rachunek.';
    default:
      return '';
  }
}
