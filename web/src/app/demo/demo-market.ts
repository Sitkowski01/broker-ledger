/**
 * Rynek trybu demo: ceny szesciu spolek Parkietu, zaczynajace od notowan zapisanych z prawdziwej gieldy
 * (16.10.2026, boty Parkietu), dalej bladzenie losowe z ziarnem — ta sama historia przy kazdym otwarciu.
 * Wszystko w groszach.
 */

export const DEMO_SYMBOLS = ['ALE', 'CDR', 'KGH', 'PKN', 'PKO', 'PZU'] as const;

/** Ostatnie ceny z Parkietu z sesji 16.10.2026 (GET /api/market/quotes). */
const RECORDED: Readonly<Record<string, number>> = { ALE: 5_535, CDR: 25_857, KGH: 32_064, PKN: 15_473, PKO: 11_483, PZU: 7_294 };

/** Deterministyczny generator liczb z ziarnem (mulberry32): powtarzalna historia cen i testy. */
export function random(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4_294_967_296;
  };
}

export interface PricePoint {
  readonly at: number;
  readonly cents: number;
}

/** Kolejna cena: ruch do ±0,6% wokol poprzedniej (plus staly dryf), w pelnych groszach, nigdy ponizej grosza. */
export function step(cents: number, rnd: () => number, scale = 0.006, drift = 0): number {
  return Math.max(1, Math.round(cents * (1 + (rnd() * 2 - 1) * scale + drift)));
}

export class DemoMarket {
  private readonly rnd: () => number;
  private readonly paths = new Map<string, PricePoint[]>();

  /**
   * Historia od <code>from</code> do <code>now</code> co godzine, tak zeby ostatnia cena byla rowna nagranej —
   * wykres wyniku konczy sie na cenach, ktore widac w pasku notowan.
   */
  constructor(from: number, now: number, seed = 16_10_2026) {
    this.rnd = random(seed);
    const hours = Math.max(1, Math.floor((now - from) / 3_600_000));
    for (const symbol of DEMO_SYMBOLS) {
      // Idziemy wstecz od nagranej ceny, a potem odwracamy — koniec sciezki to dokladnie nagranie.
      const back: number[] = [RECORDED[symbol]];
      for (let i = 0; i < hours; i++) {
        // Wstecz lekko w dol: patrzac do przodu rynek przez ostatnie tygodnie rosl, jak lubia prezentacje.
        back.push(step(back[back.length - 1], this.rnd, 0.004, -0.0002));
      }
      this.paths.set(symbol, back.reverse().map((cents, i) => ({ at: now - (hours - i) * 3_600_000, cents })));
    }
  }

  /** Ostatnia cena nie pozniejsza niz <code>at</code>; przed historia — pierwsza znana. */
  priceAt(symbol: string, at: number): number {
    const path = this.paths.get(symbol) ?? [];
    let found = path[0]?.cents ?? 0;
    for (const p of path) {
      if (p.at > at) {
        break;
      }
      found = p.cents;
    }
    return found;
  }

  last(symbol: string): number {
    const path = this.paths.get(symbol) ?? [];
    return path[path.length - 1]?.cents ?? 0;
  }

  previous(symbol: string): number | null {
    const path = this.paths.get(symbol) ?? [];
    return path.length > 1 ? path[path.length - 2].cents : null;
  }

  lastAt(symbol: string): number {
    const path = this.paths.get(symbol) ?? [];
    return path[path.length - 1]?.at ?? 0;
  }

  /** Jedna transakcja innych uczestnikow na kazdej spolce — rynek zyje, gdy demo jest otwarte. */
  tick(now: number): void {
    for (const symbol of DEMO_SYMBOLS) {
      this.paths.get(symbol)!.push({ at: now, cents: step(this.last(symbol), this.rnd) });
    }
  }
}
