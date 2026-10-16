import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Session } from '../auth/session';
import { ClientEvent, ClientEvents, EVENTS_URL } from './client-events';

describe('ClientEvents', () => {
  let backend: HttpTestingController;

  function setup(loggedIn = true) {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    if (loggedIn) {
      TestBed.inject(Session).start({ token: 't', email: 'jan@example.com', expiresAt: new Date(Date.now() + 3_600_000).toISOString() });
    }
    const events = TestBed.inject(ClientEvents);
    const seen: ClientEvent[] = [];
    const subscription = events.events$.subscribe((e) => seen.push(e));
    TestBed.tick();
    return { events, seen, subscription };
  }

  /** Kolejny kawalek strumienia: HttpClient z fetch podaje caly dotad odebrany tekst. */
  function chunk(request: TestRequest, partialText: string) {
    request.event({ type: HttpEventType.DownloadProgress, loaded: partialText.length, partialText });
  }

  afterEach(() => {
    vi.useRealTimers();
    backend.verify();
    sessionStorage.clear();
  });

  it('goes live on "ready" and turns signals into events, also when one arrives in pieces', () => {
    const { events, seen, subscription } = setup();
    const stream = backend.expectOne(EVENTS_URL);
    expect(stream.request.responseType).toBe('text');
    expect(events.status()).toBe('connecting');

    chunk(stream, 'event: ready\ndata: {}\n\n');
    expect(events.status()).toBe('live');
    chunk(stream, 'event: ready\ndata: {}\n\nevent: order\ndata: {"orderId":"o1","sta');
    chunk(stream, 'event: ready\ndata: {}\n\nevent: order\ndata: {"orderId":"o1","status":"accepted"}\n\nevent: ping\ndata: {}\n\nevent: ledger\ndata: {}\n\n');

    expect(seen).toEqual([{ kind: 'order', orderId: 'o1', status: 'accepted' }, { kind: 'ledger' }]);
    subscription.unsubscribe();
  });

  it('passes on a server resync', () => {
    const { seen, subscription } = setup();
    chunk(backend.expectOne(EVENTS_URL), 'event: ready\ndata: {}\n\nevent: resync\ndata: {}\n\n');
    expect(seen).toEqual([{ kind: 'resync' }]);
    subscription.unsubscribe();
  });

  it('reconnects after the stream ends and asks the screens to read everything again', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] });
    const { events, seen, subscription } = setup();
    const first = backend.expectOne(EVENTS_URL);
    chunk(first, 'event: ready\ndata: {}\n\n');
    first.flush('event: ready\ndata: {}\n\n');
    expect(events.status()).toBe('offline');

    vi.advanceTimersByTime(1_000);
    const second = backend.expectOne(EVENTS_URL);
    chunk(second, 'event: ready\ndata: {}\n\n');
    expect(events.status()).toBe('live');
    expect(seen).toEqual([{ kind: 'resync' }]);

    // Siec padla: znow od 1 s, bo udane polaczenie (sygnal resync) wyzerowalo licznik prob.
    second.error(new ProgressEvent('error'));
    vi.advanceTimersByTime(1_000);
    backend.expectOne(EVENTS_URL);
    subscription.unsubscribe();
  });

  it('does not connect without a session', () => {
    const { subscription } = setup(false);
    backend.expectNone(EVENTS_URL);
    subscription.unsubscribe();
  });

  it('disconnects on logout', () => {
    const { events, subscription: second } = setup();
    const stream = backend.expectOne(EVENTS_URL);
    TestBed.inject(Session).end();
    TestBed.tick();
    expect(stream.cancelled).toBe(true);
    expect(events.status()).toBe('offline');
    second.unsubscribe();
  });
});
