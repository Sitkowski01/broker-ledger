import { TestBed } from '@angular/core/testing';
import { Session } from './session';

describe('Session', () => {
  const inAnHour = () => new Date(Date.now() + 3_600_000).toISOString();

  beforeEach(() => {
    sessionStorage.clear();
    vi.useFakeTimers();
  });

  afterEach(() => vi.useRealTimers());

  it('keeps the session across a page reload', () => {
    TestBed.inject(Session).start({ token: 't', email: 'jan@example.com', expiresAt: inAnHour() });
    TestBed.resetTestingModule();

    const reloaded = TestBed.inject(Session);

    expect(reloaded.isActive()).toBe(true);
    expect(reloaded.current()?.email).toBe('jan@example.com');
  });

  it('expires on its own when the token runs out', () => {
    const session = TestBed.inject(Session);
    session.start({ token: 't', email: 'jan@example.com', expiresAt: inAnHour() });

    vi.advanceTimersByTime(3_600_000);

    expect(session.isActive()).toBe(false);
    expect(session.expired()).toBe(true);
    expect(sessionStorage.length).toBe(0);
  });

  it('drops a stored token that is already out of date', () => {
    sessionStorage.setItem(
      'broker-ledger.session',
      JSON.stringify({ token: 't', email: 'a@b.pl', expiresAt: new Date(Date.now() - 1).toISOString() }),
    );

    const session = TestBed.inject(Session);

    expect([session.isActive(), session.expired()]).toEqual([false, true]);
  });

  it('logging out is not an expiry', () => {
    const session = TestBed.inject(Session);
    session.start({ token: 't', email: 'a@b.pl', expiresAt: inAnHour() });

    session.end();

    expect([session.isActive(), session.expired()]).toEqual([false, false]);
  });

  it('survives storage that throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(TestBed.inject(Session).isActive()).toBe(false);
  });
});
