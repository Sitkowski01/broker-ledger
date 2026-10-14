import fc from 'fast-check';
import { formatCents, formatPrice, formatQuantity, formatSigned, parseCents, toApiAmount } from './money';

describe('money', () => {
  it('reads API and typed amounts into whole grosze', () => {
    expect(parseCents('8214.36')).toBe(821436);
    expect(parseCents('250,4')).toBe(25040);
    expect(parseCents(' 1 000,00 ')).toBe(100000);
    expect(parseCents('-430.00')).toBe(-43000);
    expect(parseCents('7')).toBe(700);
    // Zmiany sald w historii przychodza ze znakiem z obu stron: "+1000.00" / "-250.40".
    expect(parseCents('+1000.00')).toBe(100000);
  });

  it.each(['', 'abc', '1.234', '1,2,3', '12.', '--5', '+-5', '9'.repeat(20)])('refuses %j instead of guessing', (value) => {
    expect(parseCents(value)).toBeNull();
  });

  it('groups every thousand, also four-digit amounts', () => {
    expect(formatCents(821436)).toBe('8 214,36');
    expect(formatCents(4244653)).toBe('42 446,53');
    expect(formatCents(5)).toBe('0,05');
    expect(formatCents(-43000)).toBe('−430,00');
  });

  it('groups whole quantities the same way as amounts', () => {
    expect(formatQuantity(60)).toBe('60');
    expect(formatQuantity(1200)).toBe('1 200');
    expect(formatQuantity(1234567)).toBe('1 234 567');
  });

  it('signs results and leaves unreadable values alone', () => {
    expect(formatSigned('212.00')).toBe('+212,00');
    expect(formatSigned('-430')).toBe('−430,00');
    expect(formatSigned('0')).toBe('0,00');
    expect(formatPrice('248.4')).toBe('248,40');
    expect(formatPrice('n/a')).toBe('n/a');
    expect(formatSigned('n/a')).toBe('n/a');
  });

  it('any amount survives the trip to the API format and back', () => {
    fc.assert(
      fc.property(fc.integer({ min: -1e13, max: 1e13 }), (cents) => {
        expect(parseCents(toApiAmount(cents))).toBe(cents);
        expect(parseCents(formatCents(cents).replace('−', '-'))).toBe(cents);
      }),
    );
  });
});
