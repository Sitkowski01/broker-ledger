import { PerformancePointDto } from '../../core/account/account-api';
import { warsawDay } from '../../core/format/dates';
import { formatCents, MINUS, parseCents } from '../../core/format/money';

/**
 * Geometria wykresu wyniku bez Angulara. SVG ma viewBox 300 x 100 rozciagany na szerokosc panelu;
 * pionowo wykres zajmuje 10-90%, zeby kropki i podpisy nie wychodzily za ramke.
 */

export const WIDTH = 300;
const TOP_Y = 10;
const SPAN_Y = 80;

export interface ChartPoint {
  /** Pozycja kropki w procentach wysokosci (do CSS). */
  readonly y: number;
  readonly x: number;
  /** Polozenie kropki w jej kolumnie najechania (kolumny dziela szerokosc rowno, punkty leza od krawedzi do krawedzi). */
  readonly left: number;
  readonly day: string;
  readonly value: string;
}

export interface ChartGeometry {
  readonly line: string;
  readonly area: string;
  /** Wysokosc linii zera w procentach. */
  readonly zeroY: number;
  readonly topLabel: string;
  readonly points: readonly ChartPoint[];
  readonly trend: 'up' | 'down' | 'flat';
  /** Wynik na koniec okresu ze znakiem: "+446,53 zł". */
  readonly last: string;
  readonly since: string;
  readonly axis: readonly [string, string, string];
}

/** "+446,53 zł", "−12,00 zł", "0,00 zł". */
export function signedZl(cents: number): string {
  const text = `${formatCents(Math.abs(cents))} zł`;
  return cents > 0 ? `+${text}` : cents < 0 ? `${MINUS}${text}` : text;
}

/** Zaokraglenie granicy osi do "ladnej" kwoty: 1, 2 albo 5 razy potega dziesieciu (w groszach). */
export function niceCeil(cents: number): number {
  if (cents <= 0) {
    return 0;
  }
  const power = 10 ** Math.floor(Math.log10(cents));
  const step = [1, 2, 5, 10].map((m) => m * power).find((s) => s >= cents) ?? 10 * power;
  return step;
}

export function chartGeometry(points: readonly PerformancePointDto[], now: Date): ChartGeometry | null {
  if (points.length === 0) {
    return null;
  }
  const values = points.map((p) => parseCents(p.result) ?? 0);
  let top = niceCeil(Math.max(...values, 0));
  const bottom = -niceCeil(-Math.min(...values, 0));
  if (top === bottom) {
    // Sam zerowy wynik (np. same wplaty): os do 100 zl, zeby linia lezala na zerze, a nie na srodku.
    top = 10_000;
  }
  const yOf = (cents: number) => TOP_Y + ((top - cents) / (top - bottom)) * SPAN_Y;
  const xOf = (i: number) => (points.length === 1 ? WIDTH : (i / (points.length - 1)) * WIDTH);
  const today = warsawDay(now);
  const chartPoints = points.map((p, i) => {
    const day = warsawDay(new Date(p.at));
    return {
      x: xOf(i),
      left: points.length === 1 ? 100 : round((i / (points.length - 1)) * 100),
      y: round(yOf(values[i])),
      day: i === points.length - 1 ? 'teraz' : day === today ? 'dziś' : day.slice(0, 5),
      value: signedZl(values[i]),
    };
  });
  const line = chartPoints.map((p, i) => `${i ? 'L' : 'M'}${round(p.x)} ${p.y}`).join(' ');
  // Pojedynczy punkt: krotka kreska do niego od lewej, zeby bylo widac poziom.
  const drawn = points.length === 1 ? `M0 ${chartPoints[0].y} L${WIDTH} ${chartPoints[0].y}` : line;
  const last = values.at(-1)!;
  const first = points[0];
  return {
    line: drawn,
    area: `${drawn} L${WIDTH} 100 L0 100 Z`,
    zeroY: round(yOf(0)),
    topLabel: signedZl(top),
    points: chartPoints,
    trend: last > 0 ? 'up' : last < 0 ? 'down' : 'flat',
    last: signedZl(last),
    since: warsawDay(new Date(first.at)).slice(0, 5),
    // Przy jednym-dwoch punktach podpis srodka (i poczatku) powtarzalby "teraz".
    axis: [
      chartPoints.length > 1 ? chartPoints[0].day : '',
      chartPoints.length > 2 ? chartPoints[Math.floor((chartPoints.length - 1) / 2)].day : '',
      'teraz',
    ],
  };
}

function round(n: number): number {
  return Math.round(n * 100) / 100;
}
