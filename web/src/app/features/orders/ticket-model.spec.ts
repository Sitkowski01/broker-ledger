import fc from 'fast-check';
import {
  buyBlockCents,
  commissionCents,
  ctaText,
  MAX_QUANTITY,
  maxBuyQuantity,
  parseQuantity,
  summaryRows,
  ticketErrors,
  TicketInput,
  ticketSummary,
} from './ticket-model';
import { plainDeep as plain } from './testing';

const ticket = (overrides: Partial<TicketInput> = {}): TicketInput => ({
  side: 'buy',
  type: 'limit',
  limitCents: 25_000,
  quantity: 10,
  availableCents: 1_000_000,
  heldShares: 0,
  ...overrides,
});

describe('ticket model', () => {
  describe('commission and block (same as Commission.Standard and Order.BuyBlock)', () => {
    it('charges 0.39% rounded up to a grosz, at least 3 zl', () => {
      expect(commissionCents(250_000)).toBe(975);
      expect(commissionCents(10_000)).toBe(300);
      expect(commissionCents(76_923)).toBe(300);
      expect(commissionCents(76_924)).toBe(301);
      expect(commissionCents(100_001)).toBe(391);
    });

    it('blocks the value at the limit plus commission', () => {
      expect(buyBlockCents(25_000, 10)).toBe(250_975);
      expect(buyBlockCents(4_120, 25)).toBe(103_402);
    });

    it('finds the largest quantity that fits, commission included', () => {
      expect(maxBuyQuantity(821_436, 25_000)).toBe(32);
      expect(maxBuyQuantity(1_000, 100)).toBe(7);
      expect(maxBuyQuantity(299, 1)).toBe(0);
      expect(maxBuyQuantity(1_000_000, 0)).toBe(0);
    });

    it('never offers a maximum the ledger would refuse, nor leaves a whole share on the table', () => {
      fc.assert(
        fc.property(fc.integer({ min: 0, max: 10 ** 10 }), fc.integer({ min: 1, max: 10 ** 7 }), (available, limit) => {
          const max = maxBuyQuantity(available, limit);
          const fits = max === 0 || buyBlockCents(limit, max) <= available;
          return fits && (max === MAX_QUANTITY || buyBlockCents(limit, max + 1) > available);
        }),
      );
    });
  });

  it('reads a whole number of shares, spaces allowed', () => {
    expect(parseQuantity('1 200')).toBe(1200);
    expect(parseQuantity(' 7 ')).toBe(7);
    expect(parseQuantity('2,5')).toBeNull();
    expect(parseQuantity('-1')).toBeNull();
    expect(parseQuantity('')).toBeNull();
    expect(parseQuantity('9'.repeat(20))).toBeNull();
  });

  describe('ticket errors', () => {
    it('accepts an order that fits', () => {
      expect(ticketErrors(ticket())).toEqual({ price: null, quantity: null, short: false });
    });

    it('needs a positive limit and quantity', () => {
      expect(ticketErrors(ticket({ limitCents: null })).price).toContain('Podaj limit');
      expect(ticketErrors(ticket({ limitCents: 0 })).price).toContain('Podaj limit');
      expect(plain(ticketErrors(ticket({ limitCents: 1_000_000_001 })).price)).toBe('Limit najwyżej 10 000 000,00 zł.');
      expect(ticketErrors(ticket({ quantity: 0 })).quantity).toContain('co najmniej 1');
      expect(plain(ticketErrors(ticket({ quantity: 1_000_000_001 })).quantity)).toBe('Najwyżej 1 000 000 000 akcji w jednym zleceniu.');
    });

    it('says how much money is missing for a buy', () => {
      expect(plain(ticketErrors(ticket({ availableCents: 250_000 })))).toEqual({
        price: null,
        quantity: 'Brakuje 9,75 zł — zmniejsz ilość albo limit.',
        short: true,
      });
    });

    it('limits a sell to the shares the client holds', () => {
      expect(ticketErrors(ticket({ side: 'sell', heldShares: 6 }))).toMatchObject({ quantity: 'Masz tylko 6 szt. do sprzedaży.', short: true });
      expect(ticketErrors(ticket({ side: 'sell', heldShares: 0 })).quantity).toBe('Nie masz akcji tej spółki do sprzedaży.');
      expect(ticketErrors(ticket({ side: 'sell', heldShares: 10, availableCents: 0 })).quantity).toBeNull();
    });

    it('does not need a price for a market sell', () => {
      expect(ticketErrors(ticket({ side: 'sell', type: 'market', limitCents: null, heldShares: 10 }))).toEqual({ price: null, quantity: null, short: false });
    });
  });

  describe('ticket summary', () => {
    it('shows value, commission and block for a buy', () => {
      expect(ticketSummary(ticket(), null)).toEqual({ valueCents: 250_000, commissionCents: 975, blockCents: 250_975, estimated: false });
    });

    it('blocks shares, not money, for a sell', () => {
      expect(ticketSummary(ticket({ side: 'sell' }), null).blockCents).toBeNull();
    });

    it('estimates a market sell at the last price', () => {
      expect(ticketSummary(ticket({ side: 'sell', type: 'market', limitCents: null }), 24_840)).toEqual({
        valueCents: 248_400,
        commissionCents: 969,
        blockCents: null,
        estimated: true,
      });
      expect(ticketSummary(ticket({ side: 'sell', type: 'market' }), null).valueCents).toBeNull();
      expect(ticketSummary(ticket({ quantity: null }), null).valueCents).toBeNull();
    });
  });

  describe('summary rows', () => {
    it('shows what a buy blocks and what stays free', () => {
      expect(plain(summaryRows(ticket(), null))).toEqual({
        value: '2 500,00 zł',
        commission: '9,75 zł',
        blockLabel: 'Do zablokowania',
        block: '2 509,75 zł',
        afterLabel: 'Zostanie dostępne',
        after: '7 490,25 zł',
      });
      expect(summaryRows(ticket({ availableCents: 100 }), null).after).toBe('0,00 zł');
      expect(summaryRows(ticket({ quantity: null }), null)).toMatchObject({ value: '—', block: '—', after: '—' });
    });

    it('shows shares blocked by a sell and an estimated proceeds for a market sell', () => {
      expect(plain(summaryRows(ticket({ side: 'sell', type: 'market', limitCents: null }), 24_840))).toEqual({
        value: '≈ 2 484,00 zł',
        commission: '≈ 9,69 zł',
        blockLabel: 'Blokada akcji',
        block: '10 szt.',
        afterLabel: 'Wpływ po prowizji',
        after: '≈ 2 474,31 zł',
      });
      expect(summaryRows(ticket({ side: 'sell', quantity: 0 }), null)).toMatchObject({ block: '—', after: '—' });
    });
  });

  it('names the button after the order', () => {
    expect(plain(ctaText('buy', 1200, 'CDR'))).toBe('Kup 1 200 szt. CDR');
    expect(ctaText('sell', 5, 'PKN')).toBe('Sprzedaj 5 szt. PKN');
    expect(ctaText('buy', null, 'CDR')).toBe('Kup');
    expect(ctaText('sell', 0, 'CDR')).toBe('Sprzedaj');
  });
});
