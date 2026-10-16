import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Provider, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ClientEvent, ClientEvents, EVENT_BATCH_MS } from '../../core/events/client-events';
import { OrderDto } from '../../core/orders/order-api';
import { BUSY_REFRESH_MS, RESTING_REFRESH_MS } from './order-model';
import { OrdersPage } from './orders-page';
import { anOrder } from './testing';

describe('OrdersPage', () => {
  let backend: HttpTestingController;

  const balances = [
    { asset: 'PLN', kind: 'Cash', available: '8214.36', blocked: '2509.75' },
    { asset: 'CDR', kind: 'Shares', available: '60', blocked: '0' },
  ];

  async function render(providers: Provider[] = []) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ...providers] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OrdersPage);
    TestBed.tick();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, stable: () => fixture.whenStable() };
  }

  function respond(orders: OrderDto[] = [anOrder()]) {
    TestBed.tick();
    const list = backend.expectOne((r) => r.url === '/api/me/orders');
    expect(list.request.params.get('limit')).toBe('50');
    list.flush(orders);
    backend.expectOne('/api/me/balances').flush(balances);
  }

  afterEach(() => {
    vi.useRealTimers();
    backend.verify();
  });

  it('shows the ticket with free cash and held shares, and the order list', async () => {
    const page = await render();
    expect(page.el.querySelector('[aria-busy="true"]')).not.toBeNull();
    respond();
    await page.stable();

    expect(page.el.querySelectorAll('bl-order-list tbody tr')).toHaveLength(1);
    // Maks. przy limicie 250 zl z wolnych 8 214,36 zl.
    const price = page.el.querySelector<HTMLInputElement>('#price')!;
    price.value = '250';
    price.dispatchEvent(new Event('input'));
    await page.stable();
    expect(page.el.querySelector('.mini.num')?.textContent).toContain('Maks. 32');
  });

  it('refreshes a moving saga often, a resting order rarely, and stops once all are finished', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const page = await render();
    respond([anOrder({ status: 'unknown', exchangeOrderId: null })]);
    await page.stable();

    vi.advanceTimersByTime(BUSY_REFRESH_MS);
    respond([anOrder()]);
    await page.stable();

    // Przyjete zlecenie tylko czeka na gieldzie — nie pytamy co 3 s.
    vi.advanceTimersByTime(BUSY_REFRESH_MS);
    TestBed.tick();
    backend.expectNone((r) => r.url === '/api/me/orders');
    vi.advanceTimersByTime(RESTING_REFRESH_MS - BUSY_REFRESH_MS);
    respond([anOrder({ status: 'filled', filledQuantity: 10, fundsReleased: true })]);
    await page.stable();

    vi.advanceTimersByTime(RESTING_REFRESH_MS * 2);
    TestBed.tick();
    backend.expectNone((r) => r.url === '/api/me/orders');
  });

  it('reloads once per burst of server signals and does not poll while the stream is live', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const signals = new Subject<ClientEvent>();
    const page = await render([{ provide: ClientEvents, useValue: { events$: signals, status: signal('live') } }]);
    respond([anOrder({ status: 'unknown', exchangeOrderId: null })]);
    await page.stable();

    vi.advanceTimersByTime(BUSY_REFRESH_MS * 2);
    TestBed.tick();
    backend.expectNone((r) => r.url === '/api/me/orders');

    // Blokada, przyjecie i transakcja przychodza seria — jedno odswiezenie.
    signals.next({ kind: 'ledger' });
    signals.next({ kind: 'order', orderId: anOrder().id, status: 'accepted' });
    signals.next({ kind: 'ledger' });
    vi.advanceTimersByTime(EVENT_BATCH_MS);
    respond([anOrder()]);
    await page.stable();
    expect(page.el.querySelector('bl-order-list .status')?.textContent).toContain('Przyjęte');
  });

  it('reloads after a placed order', async () => {
    const page = await render();
    respond();
    await page.stable();
    const price = page.el.querySelector<HTMLInputElement>('#price')!;
    price.value = '250';
    price.dispatchEvent(new Event('input'));
    await page.stable();
    page.el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await page.stable();
    backend.expectOne((r) => r.method === 'POST').flush(anOrder(), { status: 201, statusText: 'Created' });
    respond([anOrder(), anOrder({ id: 'b' })]);
    await page.stable();
    expect(page.el.querySelectorAll('bl-order-list tbody tr')).toHaveLength(2);
  });

  it('offers a retry when the list cannot be loaded', async () => {
    const page = await render();
    TestBed.tick();
    backend.expectOne((r) => r.url === '/api/me/orders').flush(null, { status: 0, statusText: 'Unknown Error' });
    backend.expectOne('/api/me/balances').flush(balances);
    await page.stable();
    expect(page.el.querySelector('[role="alert"]')?.textContent).toContain('Brak połączenia');

    page.el.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    respond();
    await page.stable();
    expect(page.el.querySelector('[role="alert"]')).toBeNull();
  });
});
