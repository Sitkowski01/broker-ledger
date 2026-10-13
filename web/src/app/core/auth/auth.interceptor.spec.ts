import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { authInterceptor } from './auth.interceptor';
import { Session } from './session';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let session: Session;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    session = TestBed.inject(Session);
    session.start({ token: 'abc', email: 'a@b.pl', expiresAt: new Date(Date.now() + 60_000).toISOString() });
  });

  afterEach(() => backend.verify());

  it('sends the token to the account API but not to login', () => {
    http.get('/api/me/balances').subscribe();
    http.post('/api/auth/login', {}).subscribe();

    expect(backend.expectOne('/api/me/balances').request.headers.get('Authorization')).toBe('Bearer abc');
    expect(backend.expectOne('/api/auth/login').request.headers.has('Authorization')).toBe(false);
  });

  it('ends the session and goes to login when the server rejects the token', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.get('/api/me/orders').subscribe({ error: () => undefined });

    backend.expectOne('/api/me/orders').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect([session.isActive(), session.expired()]).toEqual([false, true]);
    expect(navigate).toHaveBeenCalledWith(['/zaloguj'], { queryParams: { powrot: '/' } });
  });

  it('leaves other errors to the screen', () => {
    let status = 0;
    http.get('/api/me/orders').subscribe({ error: (e) => (status = e.status) });

    backend.expectOne('/api/me/orders').flush(null, { status: 503, statusText: 'Unavailable' });

    expect([status, session.isActive()]).toEqual([503, true]);
  });
});

describe('authGuard', () => {
  const run = (url: string) =>
    TestBed.runInInjectionContext(() => authGuard({} as never, { url } as never));

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it('sends a guest to login and remembers where they were going', () => {
    expect(String(run('/zlecenia'))).toBe('/zaloguj?powrot=%2Fzlecenia');
    expect(String(run('/'))).toBe('/zaloguj');
  });

  it('lets a signed-in client through', () => {
    TestBed.inject(Session).start({ token: 't', email: 'a@b.pl', expiresAt: new Date(Date.now() + 60_000).toISOString() });

    expect(run('/zlecenia')).toBe(true);
  });
});
