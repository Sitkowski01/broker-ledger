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

export type OrderFilter = 'all' | 'active' | 'done';

export function matchesFilter(order: Pick<OrderDto, 'status'>, filter: OrderFilter): boolean {
  return filter === 'all' || (filter === 'active') === isActive(order);
}

export interface OrderRow {
  readonly id: string;
  readonly symbol: string;
  readonly side: OrderSide;
  readonly sideLabel: string;
  readonly typeLabel: string;
  readonly limit: string;
  readonly fill: string;
  /** Szerokosc paska wykonania w CSS, np. "40%". */
  readonly fillWidth: string;
  readonly complete: boolean;
  readonly status: string;
  readonly tone: Tone;
  /** Co jeszcze stoi pod zleceniem; po zwolnieniu blokady "—". */
  readonly block: string;
  readonly at: string;
  readonly cancellable: boolean;
}

/** Wiersz listy zlecen: wszystko juz sformatowane, szablon tylko wyswietla. */
export function orderRow(o: OrderDto, time: (iso: string) => string): OrderRow {
  const status = statusOf(o);
  const holding = o.fundsBlocked && !o.fundsReleased;
  return {
    id: o.id,
    symbol: o.symbol,
    side: o.side,
    sideLabel: o.side === 'buy' ? 'Kupno' : 'Sprzedaż',
    typeLabel: o.type === 'limit' ? 'Z limitem' : 'PKC',
    limit: o.limitPrice ? formatPrice(o.limitPrice) : '—',
    fill: `${formatQuantity(o.filledQuantity)} / ${formatQuantity(o.quantity)}`,
    fillWidth: `${o.quantity > 0 ? Math.floor((o.filledQuantity * 100) / o.quantity) : 0}%`,
    complete: o.filledQuantity >= o.quantity,
    status: status.label,
    tone: status.tone,
    block: holding ? blockedText(o) : '—',
    at: o.createdAt ? time(o.createdAt) : '—',
    cancellable: canCancel(o),
  };
}

/** Tytul osi przebiegu: "CDR · kupno 10 szt. po 250,00 zł". */
export function orderTitle(o: OrderDto): string {
  const side = o.side === 'buy' ? 'kupno' : 'sprzedaż';
  const price = o.limitPrice ? ` po ${formatPrice(o.limitPrice)} zł` : ' PKC';
  return `${o.symbol} · ${side} ${formatQuantity(o.quantity)} szt.${price}`;
}

/** Co ile odswiezac liste przy stanach przejsciowych — saga i reconciler koncza je w sekundy. */
export const BUSY_REFRESH_MS = 3_000;
/** Zlecenie przyjete potrafi czekac na gieldzie godzinami — wtedy wystarczy rzadziej. */
export const RESTING_REFRESH_MS = 15_000;

/**
 * Po ilu ms od ostatniego odswiezenia zapytac znowu; null = nie pytac (wszystko zakonczone).
 * Limit API to 120 zapytan na minute na klienta, wiec nie odpytujemy co 3 s zlecenia, ktore tylko czeka.
 */
export function refreshDelay(orders: readonly Pick<OrderDto, 'status' | 'fundsBlocked' | 'fundsReleased'>[]): number | null {
  const busy = orders.some(
    (o) => o.status === 'pending' || o.status === 'unknown' || o.status === 'cancelrequested' || (!isActive(o) && o.fundsBlocked && !o.fundsReleased),
  );
  if (busy) {
    return BUSY_REFRESH_MS;
  }
  return orders.some(isActive) ? RESTING_REFRESH_MS : null;
}

/** Potwierdzenie po zlozeniu: co poszlo, ile zablokowano i co dalej. */
export function placedMessage(o: OrderDto): string {
  const what = `${o.symbol} · ${o.side === 'buy' ? 'kupno' : 'sprzedaż'} ${formatQuantity(o.quantity)} szt.`;
  switch (o.status) {
    case 'unknown':
      return `${what}: giełda nie odpowiedziała na czas. Zablokowano ${blockedText(o)} — reconciler wyjaśni stan w tle.`;
    case 'pending':
      return `${what}: zlecenie w drodze. Zablokowano ${blockedText(o)}.`;
    default:
      return `${what}: przyjęte przez giełdę${o.exchangeOrderId ? ` (nr ${o.exchangeOrderId})` : ''}. Zablokowano ${blockedText(o)}.`;
  }
}
