import { PerformancePointDto } from '../../core/account/account-api';
import { chartGeometry, niceCeil, signedZl } from './result-chart-model';
import { plain } from './testing';

const point = (at: string, result: string): PerformancePointDto => ({ at, value: '0.00', invested: '0.00', result });
const now = new Date('2026-10-16T18:00:00Z');

describe('result chart model', () => {
  it('rounds the axis up to 1, 2 or 5 times a power of ten', () => {
    expect([1, 99, 150, 44_653, 50_000, 50_001].map(niceCeil)).toEqual([1, 100, 200, 50_000, 50_000, 100_000]);
    expect(niceCeil(0)).toBe(0);
    expect(niceCeil(-5)).toBe(0);
  });

  it('signs the result like a bank statement', () => {
    expect(plain(signedZl(44_653))).toBe('+446,53 zł');
    expect(plain(signedZl(-1_200))).toBe('−12,00 zł');
    expect(signedZl(0)).toBe('0,00 zł');
  });

  it('draws the line edge to edge with zero and the top of the axis marked', () => {
    const c = chartGeometry([point('2026-10-14T21:59:59Z', '0.00'), point('2026-10-15T21:59:59Z', '-100.00'), point('2026-10-16T18:00:00Z', '300.00')], now)!;

    // Os od −100 do +500 zl: zero na 10 + 500/600 * 80.
    expect(c.zeroY).toBe(76.67);
    expect(c.line).toBe('M0 76.67 L150 90 L300 36.67');
    expect(c.area).toBe('M0 76.67 L150 90 L300 36.67 L300 100 L0 100 Z');
    expect(plain(c.topLabel)).toBe('+500,00 zł');
    expect([c.trend, plain(c.last), c.since]).toEqual(['up', '+300,00 zł', '14.10']);
    expect(c.points.map((p) => [p.day, p.left])).toEqual([['14.10', 0], ['15.10', 50], ['teraz', 100]]);
    expect(c.axis).toEqual(['14.10', '15.10', 'teraz']);
  });

  it('shows a single point as a level line and a flat result on the zero line', () => {
    const one = chartGeometry([point('2026-10-16T18:00:00Z', '0.00')], now)!;
    expect(one.line).toBe('M0 90 L300 90');
    expect([one.zeroY, one.trend, one.points[0].left]).toEqual([90, 'flat', 100]);
    expect(one.axis).toEqual(['', '', 'teraz']);

    const loss = chartGeometry([point('2026-10-16T10:00:00Z', '-3.00'), point('2026-10-16T18:00:00Z', '-3.00')], now)!;
    expect([loss.trend, loss.points[0].day, loss.zeroY]).toEqual(['down', 'dziś', 10]);
    expect(chartGeometry([], now)).toBeNull();
  });
});
