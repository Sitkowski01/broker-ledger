import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Session } from '../auth/session';
import { QUOTE_REFRESH_MS, QuoteFeed } from './quote-feed';

describe('QuoteFeed', () => {
  let backend: HttpTestingController;
  let hidden = false;

  const quote = { symbol: 'CDR', price: '248.40', previousPrice: '248.10', at: '2026-10-13T19:42:07Z', direction: 'up' };

  // Efekty sygnalow i timer(0) startuja dopiero po ticku i przesunieciu sztucznego zegara.
  function settle() {
    TestBed.tick();
    vi.advanceTimersByTime(0);
  }

  function setVisible(visible: boolean) {
    hidden = !visible;
    document.dispatchEvent(new Event('visibilitychange'));
    settle();
  }

  beforeEach(() => {
    vi.useFakeTimers();
    sessionStorage.clear();
    hidden = false;
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => (hidden ? 'hidden' : 'visible'));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    vi.useRealTimers();
  });

  function signIn() {
    TestBed.inject(Session).start({ token: 't', email: 'a@b.pl', expiresAt: new Date(Date.now() + 3_600_000).toISOString() });
  }

  it('asks nothing while nobody is signed in', () => {
    const feed = TestBed.inject(QuoteFeed);
    settle();

    expect(feed.items()).toEqual([]);
    backend.expectNone('/api/market/quotes');
  });

  it('shows quotes for the ticker and refreshes them every 10 seconds', () => {
    signIn();
    const feed = TestBed.inject(QuoteFeed);
    settle();

    backend.expectOne('/api/market/quotes').flush([quote, { ...quote, symbol: 'ALE', price: '41.05', direction: 'down' }]);
    expect(feed.items()).toEqual([
      { label: 'CDR', value: '248,40', mark: '▲' },
      { label: 'ALE', value: '41,05', mark: '▼' },
    ]);

    vi.advanceTimersByTime(QUOTE_REFRESH_MS);
    backend.expectOne('/api/market/quotes').flush([{ ...quote, direction: 'flat' }]);
    expect(feed.items()[0].mark).toBe('•');
  });

  it('keeps the last quotes when a refresh fails', () => {
    signIn();
    const feed = TestBed.inject(QuoteFeed);
    settle();
    backend.expectOne('/api/market/quotes').flush([quote]);

    vi.advanceTimersByTime(QUOTE_REFRESH_MS);
    backend.expectOne('/api/market/quotes').flush(null, { status: 503, statusText: 'Unavailable' });

    expect(feed.items()).toHaveLength(1);
  });

  it('pauses in a hidden tab and stops after logout', () => {
    signIn();
    const feed = TestBed.inject(QuoteFeed);
    settle();
    backend.expectOne('/api/market/quotes').flush([quote]);

    setVisible(false);
    vi.advanceTimersByTime(QUOTE_REFRESH_MS * 3);
    backend.expectNone('/api/market/quotes');

    setVisible(true);
    backend.expectOne('/api/market/quotes').flush([quote]);

    TestBed.inject(Session).end();
    settle();
    vi.advanceTimersByTime(QUOTE_REFRESH_MS);
    expect(feed.items()).toEqual([]);
  });
});
