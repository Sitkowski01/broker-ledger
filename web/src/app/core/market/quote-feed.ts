import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, EMPTY, fromEvent, map, of, startWith, switchMap, timer } from 'rxjs';
import { Session } from '../auth/session';
import { formatPrice } from '../format/money';

export interface QuoteDto {
  readonly symbol: string;
  readonly price: string;
  readonly previousPrice: string | null;
  readonly at: string;
  readonly direction: 'up' | 'down' | 'flat';
}

export interface QuoteItem {
  readonly label: string;
  readonly value: string;
  readonly mark: string;
}

export const QUOTE_REFRESH_MS = 10_000;

/**
 * Notowania do paska u gory. Odpytuje API co 10 s, ale tylko gdy klient jest zalogowany
 * i karta jest na wierzchu — ukryta karta nie meczy serwera. Blad sieci zostawia ostatnie notowania.
 */
@Injectable({ providedIn: 'root' })
export class QuoteFeed {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly session = inject(Session);

  private readonly visible$ = fromEvent(this.document, 'visibilitychange').pipe(
    startWith(null),
    map(() => this.document.visibilityState !== 'hidden'),
  );

  private readonly quotes = toSignal(
    combineLatest([toObservable(this.session.isActive), this.visible$]).pipe(
      switchMap(([active, visible]) =>
        !active ? of([]) : !visible ? EMPTY : timer(0, QUOTE_REFRESH_MS).pipe(
          switchMap(() => this.http.get<QuoteDto[]>('/api/market/quotes').pipe(catchError(() => EMPTY))),
        ),
      ),
    ),
    { initialValue: [] as QuoteDto[] },
  );

  /** Ostatnia cena kazdej spolki ("248.40") — do przycisku "Ostatnia" w formularzu zlecenia. */
  readonly prices = computed<Readonly<Record<string, string>>>(() => Object.fromEntries(this.quotes().map((q) => [q.symbol, q.price])));

  readonly items = computed<QuoteItem[]>(() =>
    this.quotes().map((q) => ({
      label: q.symbol,
      value: formatPrice(q.price),
      mark: q.direction === 'up' ? '▲' : q.direction === 'down' ? '▼' : '•',
    })),
  );
}
