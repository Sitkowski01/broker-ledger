import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TransferPanel } from './transfer-panel';

describe('TransferPanel', () => {
  let backend: HttpTestingController;

  async function render(available = 821436) {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TransferPanel);
    fixture.componentRef.setInput('available', available);
    const posted = vi.fn();
    fixture.componentInstance.posted.subscribe(posted);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const type = async (value: string) => {
      const input = el.querySelector<HTMLInputElement>('#amount')!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await stable();
    };
    const click = async (label: string | RegExp) => {
      const button = [...el.querySelectorAll('button')].find((b) =>
        typeof label === 'string' ? b.textContent?.trim() === label : label.test(b.textContent ?? ''),
      )!;
      button.click();
      await stable();
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await stable();
    };
    const amount = () => el.querySelector<HTMLInputElement>('#amount')!.value;
    const text = () => el.textContent ?? '';
    return { fixture, el, type, click, submit, amount, text, posted, stable };
  }

  afterEach(() => backend.verify());

  it('deposits the typed amount with an idempotency key and tells the parent', async () => {
    const panel = await render();
    await panel.type('2 500,5');
    expect(panel.text()).toContain('Wpłać 2 500,50 zł');

    await panel.submit();
    const request = backend.expectOne('/api/me/deposits');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ amount: '2500.50', currency: 'PLN' });
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    request.flush({ outcome: 'posted' }, { status: 201, statusText: 'Created' });
    await panel.stable();

    expect(panel.posted).toHaveBeenCalledTimes(1);
    expect(panel.el.querySelector('[role="status"]')?.textContent).toBe('Wpłata 2 500,50 zł zaksięgowana.');
  });

  it('retries with the same key after a dropped connection, so nothing is booked twice', async () => {
    const panel = await render();
    await panel.submit();
    const first = backend.expectOne('/api/me/deposits');
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(null, { status: 0, statusText: 'Unknown Error' });
    await panel.stable();
    expect(panel.el.querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia z serwerem');

    await panel.submit();
    const retry = backend.expectOne('/api/me/deposits');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({ outcome: 'alreadyPosted' });
    await panel.stable();
    expect(panel.posted).toHaveBeenCalledTimes(1);

    // Kolejny przelew to nowa operacja — nowy klucz.
    await panel.submit();
    expect(backend.expectOne('/api/me/deposits').request.headers.get('Idempotency-Key')).not.toBe(key);
  });

  it('uses a new key once the amount changes', async () => {
    const panel = await render();
    await panel.submit();
    const first = backend.expectOne('/api/me/deposits');
    first.flush(null, { status: 503, statusText: 'Unavailable' });
    await panel.stable();

    await panel.type('999');
    expect(panel.el.querySelector('[role="alert"]')).toBeNull();
    await panel.submit();
    const second = backend.expectOne('/api/me/deposits');
    expect(second.request.headers.get('Idempotency-Key')).not.toBe(first.request.headers.get('Idempotency-Key'));
    second.flush({ outcome: 'posted' });
  });

  it('adds the quick amounts to a deposit', async () => {
    const panel = await render();
    await panel.click('+500');
    expect(panel.amount()).toBe('1 500,00');
    await panel.type('abc');
    await panel.click('+1 000');
    expect(panel.amount()).toBe('1 000,00');
    await panel.click('+5 000');
    expect(panel.amount()).toBe('6 000,00');
  });

  it('stops a withdrawal larger than the free cash before calling the server', async () => {
    const panel = await render();
    await panel.click('Wypłata');
    expect(panel.text()).toContain('Do wypłaty: 8 214,36 zł');
    await panel.type('8214,37');
    expect(panel.text()).not.toContain('Możesz wypłacić');

    await panel.submit();
    expect(panel.text()).toContain('Możesz wypłacić najwyżej 8 214,36 zł');
    expect(panel.el.querySelector('#amount')?.getAttribute('aria-invalid')).toBe('true');
    expect(panel.text()).toContain('Wypłać');
    backend.expectNone('/api/me/withdrawals');
  });

  it('offers parts of the free cash for a withdrawal and sends it', async () => {
    const panel = await render();
    await panel.click('Wypłata');
    await panel.click('25%');
    expect(panel.amount()).toBe('2 053,59');
    await panel.click('50%');
    expect(panel.amount()).toBe('4 107,18');
    await panel.click('Całość');
    expect(panel.amount()).toBe('8 214,36');

    await panel.submit();
    const request = backend.expectOne('/api/me/withdrawals');
    expect(request.request.body).toEqual({ amount: '8214.36', currency: 'PLN' });
    request.flush({ outcome: 'posted' }, { status: 201, statusText: 'Created' });
    await panel.stable();
    expect(panel.text()).toContain('Wypłata 8 214,36 zł zaksięgowana.');
  });

  it('shows what the ledger says when someone spent the money in the meantime', async () => {
    const panel = await render();
    await panel.click('Wypłata');
    await panel.type('100');
    await panel.submit();

    backend.expectOne('/api/me/withdrawals').flush(
      { title: 'Insufficient funds', asset: 'PLN', available: '40.00' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await panel.stable();

    expect(panel.el.querySelector('[role="alert"]')?.textContent).toBe('Za mało wolnych środków. Dostępne: 40,00 zł.');
    expect(panel.posted).not.toHaveBeenCalled();
  });

  it('shows server field errors under the amount', async () => {
    const panel = await render();
    await panel.submit();
    backend.expectOne('/api/me/deposits').flush({ errors: { amount: ['too big'] } }, { status: 400, statusText: 'Bad Request' });
    await panel.stable();
    expect(panel.el.querySelector('.field-error')?.textContent).toBe('Podaj kwotę większą od zera, np. 250,40.');

    await panel.type('5');
    await panel.submit();
    backend.expectOne('/api/me/deposits').flush({ errors: { currency: ['supported currencies: PLN'] } }, { status: 400, statusText: 'Bad Request' });
    await panel.stable();
    expect(panel.el.querySelector('.field-error')?.textContent).toBe('supported currencies: PLN');
  });

  it('sends one transfer at a time', async () => {
    const panel = await render();
    await panel.submit();
    expect(panel.text()).toContain('Księguję…');
    await panel.submit();
    backend.expectOne('/api/me/deposits').flush({ outcome: 'posted' });
  });

  it('switching the kind clears the earlier error', async () => {
    const panel = await render(0);
    await panel.click('Wypłata');
    await panel.submit();
    expect(panel.text()).toContain('Nie masz wolnych środków do wypłaty.');

    await panel.click('Wpłata');
    expect(panel.el.querySelector('.field-error')).toBeNull();
    expect(panel.text()).toContain('W demo przelew z banku księguje się od razu.');
  });

  it('lists the recent transfers with a link to the ledger', async () => {
    const panel = await render();
    panel.fixture.componentRef.setInput('recent', [{ sequence: 7, label: 'Wpłata z banku', amount: '+10 000,00', trend: 'up', at: 'dziś, 18:05' }]);
    await panel.stable();

    expect(panel.text()).toContain('Ostatnie przelewy');
    const link = panel.el.querySelector<HTMLAnchorElement>('.recent a')!;
    expect(link.getAttribute('href')).toBe('/ksiega');
    expect(link.getAttribute('aria-label')).toBe('Dokument nr 7 w księdze');
    expect(panel.el.querySelector('.recent .amount')?.classList).toContain('up');
  });

  it('ignores an error of an attempt the client already changed', async () => {
    const panel = await render();
    await panel.submit();
    const old = backend.expectOne('/api/me/deposits');
    await panel.type('200');

    old.flush(null, { status: 0, statusText: 'Unknown Error' });
    await panel.stable();

    expect(panel.el.querySelector('[role="alert"]')).toBeNull();
    expect(panel.text()).toContain('Wpłać 200,00 zł');
  });

  it('still reports a transfer that went through after the form changed', async () => {
    const panel = await render();
    await panel.submit();
    const old = backend.expectOne('/api/me/deposits');
    const key = old.request.headers.get('Idempotency-Key');
    await panel.click('Wypłata');

    old.flush({ outcome: 'posted' }, { status: 201, statusText: 'Created' });
    await panel.stable();

    expect(panel.el.querySelector('[role="status"]')?.textContent).toContain('zaksięgowana');
    expect(panel.posted).toHaveBeenCalledTimes(1);
    await panel.type('10');
    await panel.submit();
    expect(backend.expectOne('/api/me/withdrawals').request.headers.get('Idempotency-Key')).not.toBe(key);
  });
});
