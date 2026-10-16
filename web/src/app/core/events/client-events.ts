import { HttpClient, HttpDownloadProgressEvent, HttpEventType } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import {
  asyncScheduler,
  defer,
  EMPTY,
  filter,
  map,
  mergeMap,
  Observable,
  of,
  race,
  retry,
  share,
  Subject,
  switchMap,
  tap,
  throttleTime,
  timer,
} from 'rxjs';
import { Session } from '../auth/session';
import { parseSse } from './sse';

/** Sygnal z serwera: "u ciebie cos sie zmienilo". Bez kwot — po sygnale pytamy API o aktualny stan. */
export type ClientEvent =
  | { readonly kind: 'order'; readonly orderId: string; readonly status: string }
  | { readonly kind: 'ledger' }
  /** Polaczenie wrocilo po przerwie — cos moglo przepasc, wiec trzeba przeczytac wszystko od nowa. */
  | { readonly kind: 'resync' };

export type StreamStatus = 'connecting' | 'live' | 'offline';

export const EVENTS_URL = '/api/me/events';
/** Ponowienia po zerwaniu: 1 s, 2 s, 4 s... najwyzej 30 s. */
export const MAX_RETRY_MS = 30_000;
/**
 * Ekrany odswiezaja sie po sygnale najwyzej raz na okno: blokada, przyjecie i transakcje przychodza seria, a aktywny
 * klient potrafi dostac kilkadziesiat sygnalow na minute — limit API to 120 zapytan na minute.
 * Zlecenia (2 zapytania) co sekunde, ekrany z wieloma panelami rzadziej.
 */
export const EVENT_BATCH_MS = 1_000;
export const SLOW_EVENT_BATCH_MS = 4_000;

/** Pierwszy sygnal otwiera okno, a na jego koncu przychodzi jedno odswiezenie — kolejne sygnaly w oknie nic nie dodaja. */
export function batched<T>(ms: number) {
  return throttleTime<T>(ms, asyncScheduler, { leading: false, trailing: true });
}

/**
 * Strumien zdarzen klienta (Server-Sent Events) przez HttpClient z fetch: interceptor dokleja token JWT,
 * czego EventSource nie potrafi. Jedno polaczenie na aplikacje, dzielone przez wszystkie ekrany, i tylko
 * dla zalogowanego klienta. Po zerwaniu laczy sie ponownie i oglasza "resync" — w przerwie moglo cos przepasc.
 */
@Injectable({ providedIn: 'root' })
export class ClientEvents {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  private readonly state = signal<StreamStatus>('offline');
  private readonly lastHeard = signal<Date | null>(null);
  private readonly nextTry = signal<number | null>(null);
  private readonly retryNow$ = new Subject<void>();

  readonly status = this.state.asReadonly();
  /** Kiedy ostatnio slyszelismy serwer — od tej chwili dane na ekranie moga byc nieaktualne. */
  readonly lastHeardAt = this.lastHeard.asReadonly();
  /** Kiedy (ms od epoki) bedzie kolejna proba polaczenia; null, gdy polaczenie jest albo nie czekamy. */
  readonly nextAttemptAt = this.nextTry.asReadonly();

  readonly events$: Observable<ClientEvent> = toObservable(this.session.isActive).pipe(
    switchMap((active) => (active ? this.connect() : (this.state.set('offline'), this.nextTry.set(null), EMPTY))),
    share(),
  );

  /** Polacz od razu, bez czekania na kolejna probe (przycisk "Sprobuj teraz"). */
  reconnect(): void {
    this.retryNow$.next();
  }

  private connect(): Observable<ClientEvent> {
    let attempt = 0;
    return defer(() => {
      this.state.set('connecting');
      this.nextTry.set(null);
      let received = 0;
      return this.http.get(EVENTS_URL, { observe: 'events', responseType: 'text', reportProgress: true }).pipe(
        filter((e): e is HttpDownloadProgressEvent => e.type === HttpEventType.DownloadProgress),
        map((e) => {
          // partialText rosnie z kazdym kawalkiem — bierzemy tylko nowa czesc od ostatniego pelnego zdarzenia.
          const { messages, rest } = parseSse((e.partialText ?? '').slice(received));
          received = (e.partialText ?? '').length - rest.length;
          return messages;
        }),
        mergeMap((messages) => of(...messages)),
        tap(() => this.lastHeard.set(new Date())),
        mergeMap((message): Observable<ClientEvent> => {
          switch (message.event) {
            case 'ready':
              this.state.set('live');
              // Drugie i kolejne polaczenie: w przerwie moglo cos przepasc.
              return attempt++ > 0 ? of({ kind: 'resync' as const }) : EMPTY;
            case 'order': {
              const data = JSON.parse(message.data) as { orderId: string; status: string };
              return of({ kind: 'order' as const, orderId: data.orderId, status: data.status });
            }
            case 'ledger':
              return of({ kind: 'ledger' as const });
            case 'resync':
              return of({ kind: 'resync' as const });
            default:
              return EMPTY;
          }
        }),
        tap({ complete: () => this.state.set('offline'), error: () => this.state.set('offline') }),
      );
    }).pipe(
      // Serwer zamknal strumien (np. restart) albo siec padla — probujemy dalej, coraz rzadziej.
      repeatAsRetry(),
      retry({
        delay: (_, count) => {
          const wait = Math.min(1_000 * 2 ** (count - 1), MAX_RETRY_MS);
          this.nextTry.set(Date.now() + wait);
          // "Sprobuj teraz" nie czeka do konca przerwy.
          return race(timer(wait), this.retryNow$);
        },
        resetOnSuccess: true,
      }),
    );
  }
}

/** Zakonczony strumien to tez powod do ponownego polaczenia — zamieniamy "complete" na blad dla retry. */
function repeatAsRetry<T>() {
  return (source: Observable<T>) =>
    new Observable<T>((subscriber) =>
      source.subscribe({
        next: (value) => subscriber.next(value),
        error: (error: unknown) => subscriber.error(error),
        complete: () => subscriber.error(new Error('event stream closed')),
      }),
    );
}
