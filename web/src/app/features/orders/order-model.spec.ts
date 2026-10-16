import {
  blockedText,
  BUSY_REFRESH_MS,
  canCancel,
  isActive,
  matchesFilter,
  orderRow,
  orderTitle,
  placedMessage,
  refreshDelay,
  rejectionText,
  RESTING_REFRESH_MS,
  sagaNote,
  sagaSteps,
  statusOf,
} from './order-model';
import { anOrder, plainDeep as plain } from './testing';

const time = (iso: string) => `t(${iso.slice(11, 16)})`;

describe('order model', () => {
  describe('status', () => {
    it('names every status and marks partial fills', () => {
      expect(statusOf(anOrder())).toEqual({ label: 'Przyjęte', tone: 'live' });
      expect(statusOf(anOrder({ filledQuantity: 4 }))).toEqual({ label: 'Częściowo', tone: 'live' });
      expect(statusOf(anOrder({ status: 'unknown' }))).toEqual({ label: 'Nieznany', tone: 'wait' });
      expect(statusOf(anOrder({ status: 'filled' })).tone).toBe('ok');
      expect(statusOf(anOrder({ status: 'rejected' })).tone).toBe('bad');
      expect(statusOf(anOrder({ status: 'cancelled' })).label).toBe('Anulowane');
      expect(statusOf(anOrder({ status: 'cancelrequested' })).label).toBe('Anulowanie');
      expect(statusOf(anOrder({ status: 'pending' })).label).toBe('Wysyłane');
    });

    it('treats waiting orders as active and lets only accepted ones be cancelled', () => {
      const all = ['pending', 'unknown', 'accepted', 'cancelrequested', 'rejected', 'filled', 'cancelled'] as const;
      expect(all.filter((status) => isActive({ status }))).toEqual(['pending', 'unknown', 'accepted', 'cancelrequested']);
      expect(all.filter((status) => canCancel({ status }))).toEqual(['accepted']);
      expect(all.filter((status) => matchesFilter({ status }, 'done'))).toEqual(['rejected', 'filled', 'cancelled']);
      expect(all.every((status) => matchesFilter({ status }, 'all'))).toBe(true);
    });

    it('translates rejection reasons', () => {
      expect(rejectionText(null)).toBeNull();
      expect(rejectionText('insufficient funds')).toContain('Za mało wolnych środków');
      expect(rejectionText('CDR: queue full')).toBe('Giełda odrzuciła zlecenie: CDR: queue full.');
    });
  });

  describe('saga steps', () => {
    const states = (o: Parameters<typeof sagaSteps>[0]) => sagaSteps(o, time).map((s) => `${s.name}:${s.state}`);

    it('shows an accepted order waiting for trades', () => {
      expect(plain(sagaSteps(anOrder(), time))).toEqual([
        { name: 'Zapisane', note: 't(17:43)', state: 'done' },
        { name: 'Blokada', note: '2 509,75 zł', state: 'done' },
        { name: 'Przyjęte', note: 'nr giełdy 7', state: 'done' },
        { name: 'Rozliczane', note: '0 / 10 szt.', state: 'now' },
        { name: 'Zwolnione', note: '—', state: 'todo' },
      ]);
    });

    it('shows an unknown order being explained by the reconciler', () => {
      expect(states(anOrder({ status: 'unknown', exchangeOrderId: null }))).toEqual([
        'Zapisane:done', 'Blokada:done', 'Wyjaśniane:now', 'Rozliczone:todo', 'Zwolnione:todo',
      ]);
    });

    it('shows a filled order with the rest of the block returned', () => {
      expect(states(anOrder({ status: 'filled', filledQuantity: 10, fundsReleased: true }))).toEqual([
        'Zapisane:done', 'Blokada:done', 'Przyjęte:done', 'Rozliczone:done', 'Zwolnione:done',
      ]);
      expect(states(anOrder({ status: 'filled', filledQuantity: 10 }))[4]).toBe('Zwalnianie:now');
    });

    it('fails at the block when money was short and at the exchange when it refused', () => {
      expect(states(anOrder({ status: 'rejected', exchangeOrderId: null, fundsBlocked: false }))).toEqual([
        'Zapisane:done', 'Blokada:fail', 'Giełda:todo', 'Rozliczone:todo', 'Zwolnione:todo',
      ]);
      expect(states(anOrder({ status: 'rejected', exchangeOrderId: null, fundsReleased: true }))).toEqual([
        'Zapisane:done', 'Blokada:done', 'Giełda:fail', 'Rozliczone:todo', 'Zwolnione:done',
      ]);
    });

    it('shows shares as the block of a sell and a partly filled cancellation', () => {
      const sell = anOrder({ side: 'sell', type: 'market', limitPrice: null, blockedAsset: 'CDR', blocked: '200', quantity: 200, filledQuantity: 150, status: 'cancelled', fundsReleased: true });
      expect(sagaSteps(sell, time)[1].note).toBe('200 szt.');
      expect(plain(sagaSteps(sell, time)[3])).toEqual({ name: 'Rozliczone', note: '150 / 200 szt.', state: 'done' });
      expect(sagaNote(sell)).toContain('niewykonana część');
    });

    it('waits for the block and the send of a fresh order', () => {
      expect(states(anOrder({ status: 'pending', exchangeOrderId: null, fundsBlocked: false, createdAt: null }))).toEqual([
        'Zapisane:done', 'Blokada:now', 'Giełda:todo', 'Rozliczone:todo', 'Zwolnione:todo',
      ]);
      expect(sagaSteps(anOrder({ status: 'pending', exchangeOrderId: null }), time)[2]).toEqual({ name: 'Giełda', note: 'wysyłanie', state: 'now' });
    });

    it('explains the states that need a sentence', () => {
      expect(sagaNote(anOrder())).toBe('');
      expect(sagaNote(anOrder({ status: 'unknown' }))).toContain('Reconciler');
      expect(sagaNote(anOrder({ status: 'pending' }))).toContain('w drodze');
      expect(sagaNote(anOrder({ status: 'cancelrequested' }))).toContain('Kafki');
      expect(sagaNote(anOrder({ status: 'rejected', rejectionReason: 'insufficient funds' }))).toContain('nic nie zablokowano');
      expect(sagaNote(anOrder({ status: 'cancelled' }))).toBe('Zlecenie zdjęte z giełdy, blokada wróciła na rachunek.');
    });
  });

  it('formats a list row', () => {
    expect(plain(orderRow(anOrder({ filledQuantity: 4 }), time))).toEqual({
      id: anOrder().id,
      symbol: 'CDR',
      side: 'buy',
      sideLabel: 'Kupno',
      typeLabel: 'Z limitem',
      limit: '250,00',
      fill: '4 / 10',
      fillWidth: '40%',
      complete: false,
      status: 'Częściowo',
      tone: 'live',
      block: '2 509,75 zł',
      at: 't(17:43)',
      cancellable: true,
    });
    const done = orderRow(anOrder({ type: 'market', limitPrice: null, side: 'sell', status: 'filled', filledQuantity: 10, fundsReleased: true, createdAt: null }), time);
    expect([done.limit, done.block, done.at, done.typeLabel, done.sideLabel, done.complete]).toEqual(['—', '—', '—', 'PKC', 'Sprzedaż', true]);
  });

  it('titles an order', () => {
    expect(orderTitle(anOrder())).toBe('CDR · kupno 10 szt. po 250,00 zł');
    expect(plain(orderTitle(anOrder({ side: 'sell', limitPrice: null, quantity: 1200 })))).toBe('CDR · sprzedaż 1 200 szt. PKC');
  });

  it('says what was blocked and what happens next after placing', () => {
    expect(plain(blockedText(anOrder()))).toBe('2 509,75 zł');
    expect(plain(blockedText({ blockedAsset: 'CDR', blocked: '1200' }))).toBe('1 200 szt.');
    expect(plain(placedMessage(anOrder()))).toBe('CDR · kupno 10 szt.: przyjęte przez giełdę (nr 7). Zablokowano 2 509,75 zł.');
    expect(placedMessage(anOrder({ exchangeOrderId: null }))).toContain('przyjęte przez giełdę. Zablokowano');
    expect(placedMessage(anOrder({ status: 'unknown' }))).toContain('reconciler wyjaśni stan w tle');
    expect(placedMessage(anOrder({ status: 'pending', side: 'sell' }))).toContain('sprzedaż 10 szt.: zlecenie w drodze');
  });
});

describe('refresh delay', () => {
  it('asks often while the saga is moving, rarely for a resting order and never when all are done', () => {
    expect(refreshDelay([])).toBeNull();
    expect(refreshDelay([anOrder({ status: 'filled', fundsReleased: true })])).toBeNull();
    expect(refreshDelay([anOrder()])).toBe(RESTING_REFRESH_MS);
    expect(refreshDelay([anOrder(), anOrder({ status: 'unknown' })])).toBe(BUSY_REFRESH_MS);
    expect(refreshDelay([anOrder({ status: 'pending' })])).toBe(BUSY_REFRESH_MS);
    expect(refreshDelay([anOrder({ status: 'cancelrequested' })])).toBe(BUSY_REFRESH_MS);
    // Zakonczone, ale reszta blokady jeszcze nie wrocila.
    expect(refreshDelay([anOrder({ status: 'filled' })])).toBe(BUSY_REFRESH_MS);
    expect(refreshDelay([anOrder({ status: 'rejected', fundsBlocked: false })])).toBeNull();
  });
});
