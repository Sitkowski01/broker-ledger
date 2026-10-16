import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { plain, words } from '../dashboard/testing';
import { OrderTicket } from './order-ticket';
import { anOrder } from './testing';

describe('OrderTicket', () => {
  let backend: HttpTestingController;

  async function render(available = 821_436, holdings: Record<string, number> = { CDR: 60 }) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OrderTicket);
    fixture.componentRef.setInput('available', available);
    fixture.componentRef.setInput('holdings', holdings);
    fixture.componentRef.setInput('prices', { CDR: '248.40' });
    const placed = vi.fn();
    fixture.componentInstance.placed.subscribe(placed);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const type = async (id: string, value: string) => {
      const input = el.querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
      await stable();
    };
    const button = (label: string | RegExp) =>
      [...el.querySelectorAll('button')].find((b) => (typeof label === 'string' ? plain(b.textContent) === label : label.test(plain(b.textContent))))!;
    const click = async (label: string | RegExp) => {
      button(label).click();
      await stable();
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await stable();
    };
    const summary = () => words(el.querySelector('.summary'));
    const value = (id: string) => el.querySelector<HTMLInputElement>(`#${id}`)!.value;
    return { fixture, el, type, click, button, submit, summary, value, placed, stable };
  }

  afterEach(() => backend.verify());

  it('shows the block before the client clicks and sends the order with an idempotency key', async () => {
    const t = await render();
    await t.type('price', '250');

    expect(t.summary()).toBe('Wartość 2 500,00 zł Prowizja 0,39% · min 3 zł 9,75 zł Do zablokowania 2 509,75 zł Zostanie dostępne 5 704,61 zł');
    expect(plain(t.button(/^Kup \d/).textContent)).toBe('Kup 10 szt. CDR');

    await t.submit();
    const request = backend.expectOne('/api/me/orders');
    expect(request.request.body).toEqual({ symbol: 'CDR', side: 'buy', type: 'limit', limitPrice: '250.00', quantity: 10 });
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    request.flush(anOrder(), { status: 201, statusText: 'Created' });
    await t.stable();

    expect(t.placed).toHaveBeenCalledTimes(1);
    expect(plain(t.el.querySelector('[role="status"]')?.textContent)).toBe('CDR · kupno 10 szt.: przyjęte przez giełdę (nr 7). Zablokowano 2 509,75 zł.');
  });

  it('fills the last price and the largest quantity that fits', async () => {
    const t = await render();
    await t.click('Ostatnia');
    expect(plain(t.value('price'))).toBe('248,40');
    await t.click('Maks. 32');
    expect(t.value('quantity')).toBe('32');
    expect(t.el.querySelector('.field-error')).toBeNull();
  });

  it('says how much is missing as soon as the order does not fit', async () => {
    const t = await render(100_000);
    await t.type('price', '250');

    expect(plain(t.el.querySelector('#quantity-error')?.textContent)).toBe('Brakuje 1 509,75 zł — zmniejsz ilość albo limit.');
    expect(t.el.querySelector('#quantity')?.getAttribute('aria-invalid')).toBe('true');
    await t.submit();
    backend.expectNone('/api/me/orders');
  });

  it('asks for a limit only after the first try', async () => {
    const t = await render();
    expect(t.el.querySelector('#price-error')).toBeNull();
    await t.submit();
    expect(t.el.querySelector('#price-error')?.textContent).toContain('Podaj limit');
    backend.expectNone('/api/me/orders');
  });

  it('allows a market order only for a sell and blocks shares instead of money', async () => {
    const t = await render();
    expect(t.button('PKC').disabled).toBe(true);

    await t.click('Sprzedaż');
    await t.click('PKC');
    expect(t.el.querySelector('#price')).toBeNull();
    expect(t.summary()).toBe('Wartość ≈ 2 484,00 zł Prowizja 0,39% · min 3 zł ≈ 9,69 zł Blokada akcji 10 szt. Wpływ po prowizji ≈ 2 474,31 zł');
    expect(t.el.querySelector('.hint')?.textContent).toContain('Do sprzedaży: 60 szt.');

    await t.submit();
    const request = backend.expectOne('/api/me/orders');
    expect(request.request.body).toEqual({ symbol: 'CDR', side: 'sell', type: 'market', limitPrice: null, quantity: 10 });
    request.flush(anOrder({ side: 'sell', type: 'market', blockedAsset: 'CDR', blocked: '10', status: 'unknown', exchangeOrderId: null }), { status: 202, statusText: 'Accepted' });
    await t.stable();
    expect(t.el.querySelector('[role="status"]')?.textContent).toContain('reconciler wyjaśni stan w tle');

    // Powrot do kupna wraca do zlecenia z limitem.
    await t.click('Kupno');
    expect(t.el.querySelector('#price')).not.toBeNull();
  });

  it('cannot sell more than the client holds', async () => {
    const t = await render(821_436, {});
    await t.click('Sprzedaż');
    await t.type('price', '250');
    expect(t.el.querySelector('#quantity-error')?.textContent).toBe('Nie masz akcji tej spółki do sprzedaży.');
  });

  it('retries with the same key after a dropped connection and uses a new one once the form changes', async () => {
    const t = await render();
    await t.type('price', '250');
    await t.submit();
    const first = backend.expectOne('/api/me/orders');
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(null, { status: 0, statusText: 'Unknown Error' });
    await t.stable();
    expect(t.el.querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia');

    await t.submit();
    const retry = backend.expectOne('/api/me/orders');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(null, { status: 0, statusText: 'Unknown Error' });
    await t.stable();

    await t.type('quantity', '5');
    await t.submit();
    expect(backend.expectOne('/api/me/orders').request.headers.get('Idempotency-Key')).not.toBe(key);
  });

  it('shows a rejected order and still hands it to the list', async () => {
    const t = await render();
    await t.type('price', '250');
    await t.submit();
    backend.expectOne('/api/me/orders').flush(
      anOrder({ status: 'rejected', rejectionReason: 'CDR: queue full', exchangeOrderId: null, fundsReleased: true }),
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await t.stable();

    expect(t.el.querySelector('[role="alert"]')?.textContent).toBe('Giełda odrzuciła zlecenie: CDR: queue full.');
    expect(t.placed).toHaveBeenCalledTimes(1);
  });

  it('shows validation from the API under the fields', async () => {
    const t = await render();
    await t.type('price', '250');
    await t.submit();
    backend.expectOne('/api/me/orders').flush(
      { title: 'One or more validation errors occurred.', errors: { order: ['market buy is not supported'], quantity: ['quantity is required'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await t.stable();
    expect(t.el.querySelector('#quantity-error')?.textContent).toBe('Podaj liczbę akcji.');
    expect(t.el.querySelector('[role="alert"]')?.textContent).toBe('market buy is not supported');
  });

  it('changes the header and the price hint with the chosen company', async () => {
    const t = await render();
    expect(t.el.querySelector('#ticket-title')?.textContent).toBe('$CDR');
    await t.type('symbol', 'PKN');
    expect(t.el.querySelector('#ticket-title')?.textContent).toBe('$PKN');
    expect(t.el.querySelector('.last')).toBeNull();
    expect(t.el.textContent).not.toContain('Ostatnia');
  });
});
