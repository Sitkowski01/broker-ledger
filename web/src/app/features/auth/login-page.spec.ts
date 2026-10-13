import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { Session } from '../../core/auth/session';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let backend: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  async function render(returnTo?: string) {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(returnTo ? { powrot: returnTo } : {}) } } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const type = (id: string, value: string) => {
      const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    const text = () => el.textContent ?? '';
    const button = (label: string) => [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    return { el, type, submit, text, button, fixture };
  }

  afterEach(() => backend.verify());

  it('checks the form before calling the server', async () => {
    const page = await render();
    page.type('email', 'nie-mail');

    await page.submit();

    expect(page.text()).toContain('Podaj poprawny adres e-mail.');
    backend.expectNone('/api/auth/login');
  });

  it('signs in and returns to the screen the client wanted', async () => {
    const page = await render('/ksiega');
    page.type('email', 'jan@example.com');
    page.type('password', 'tajne-haslo');
    await page.submit();

    const request = backend.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ email: 'jan@example.com', password: 'tajne-haslo' });
    request.flush({ accessToken: 'jwt', expiresAt: new Date(Date.now() + 60_000).toISOString() });

    expect(TestBed.inject(Session).current()).toMatchObject({ token: 'jwt', email: 'jan@example.com' });
    expect(navigate).toHaveBeenCalledWith('/ksiega');
  });

  it('never redirects outside the app after login', async () => {
    const page = await render('//evil.example');
    page.type('email', 'jan@example.com');
    page.type('password', 'x');
    await page.submit();

    backend.expectOne('/api/auth/login').flush({ accessToken: 'jwt', expiresAt: new Date(Date.now() + 60_000).toISOString() });

    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('shows one message for a wrong e-mail or password', async () => {
    const page = await render();
    page.type('email', 'jan@example.com');
    page.type('password', 'zle-haslo');
    await page.submit();

    backend.expectOne('/api/auth/login').flush({ title: 'Invalid e-mail or password' }, { status: 401, statusText: 'Unauthorized' });
    await page.fixture.whenStable();

    expect(page.el.querySelector('[role="alert"]')?.textContent).toBe('Nieprawidłowy e-mail lub hasło.');
    expect(page.button('Zaloguj się').disabled).toBe(false);
  });

  it('registers and signs the new client straight in', async () => {
    const page = await render();
    page.button('Nowe konto').click();
    page.type('email', 'nowy@example.com');
    page.type('password', 'krotkie');
    await page.submit();
    expect(page.text()).toContain('Hasło musi mieć od 8 do 128 znaków.');

    page.type('password', 'dlugie-haslo');
    await page.submit();
    backend.expectOne('/api/auth/register').flush({ clientId: 'c' }, { status: 201, statusText: 'Created' });
    backend.expectOne('/api/auth/login').flush({ accessToken: 'jwt', expiresAt: new Date(Date.now() + 60_000).toISOString() });

    expect(TestBed.inject(Session).isActive()).toBe(true);
  });

  it('tells a returning client that the e-mail already has an account', async () => {
    const page = await render();
    page.button('Nowe konto').click();
    page.type('email', 'jan@example.com');
    page.type('password', 'dlugie-haslo');
    await page.submit();

    backend.expectOne('/api/auth/register').flush({ title: 'E-mail already registered' }, { status: 409, statusText: 'Conflict' });
    await page.fixture.whenStable();

    expect(page.text()).toContain('Ten e-mail ma już konto. Zaloguj się.');
  });
});
