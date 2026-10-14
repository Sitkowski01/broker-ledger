import { longDate, shortDateTime } from './dates';

describe('dates', () => {
  const morning = new Date('2026-10-14T08:00:00Z');

  it('says "dziś" for today and the date otherwise', () => {
    expect(shortDateTime('2026-10-14T16:05:00Z', morning)).toBe('dziś, 18:05');
    expect(shortDateTime('2026-09-14T07:12:00Z', morning)).toBe('14.09, 09:12');
  });

  it('uses Warsaw time for "today", also around midnight UTC', () => {
    // 23:30 UTC 13.10 to juz 01:30 14.10 w Warszawie.
    expect(shortDateTime('2026-10-13T23:30:00Z', morning)).toBe('dziś, 01:30');
    expect(shortDateTime('2026-10-13T21:30:00Z', morning)).toBe('13.10, 23:30');
  });

  it('does not take the same day a year earlier for today', () => {
    expect(shortDateTime('2025-10-14T08:00:00Z', morning)).toBe('14.10, 10:00');
  });

  it('names the day in a page header', () => {
    expect(longDate(new Date('2026-10-13T10:00:00Z'))).toBe('wtorek 13.10.2026');
    expect(longDate(new Date('2026-10-13T22:30:00Z'))).toBe('środa 14.10.2026');
  });
});
