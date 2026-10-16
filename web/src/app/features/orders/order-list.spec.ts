import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrderDto } from '../../core/orders/order-api';
import { plain, words } from '../dashboard/testing';
import { OrderList } from './order-list';
import { anOrder } from './testing';

describe('OrderList', () => {
  let backend: HttpTestingController;

  const accepted = anOrder();
  const filled = anOrder({ id: 'f', symbol: 'PKN', status: 'filled', filledQuantity: 10, fundsReleased: true, exchangeOrderId: 2 });
  const unknown = anOrder({ id: 'u', symbol: 'ALE', status: 'unknown', exchangeOrderId: null });

  async function render(orders: OrderDto[] = [accepted, filled, unknown]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OrderList);
    fixture.componentRef.setInput('orders', orders);
    fixture.componentRef.setInput('now', new Date('2026-10-16T18:00:00Z'));
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const click = async (label: string, root: Element = el) => {
      [...root.querySelectorAll('button')].find((b) => plain(b.textContent).startsWith(label))!.click();
      await stable();
    };
    const rows = () => [...el.querySelectorAll('tbody tr')].map((r) => r.querySelector('.sym')?.textContent);
    return { fixture, el, click, rows, changed, stable };
  }

  afterEach(() => backend.verify());

  it('lists orders with status, fill and what is still blocked', async () => {
    const l = await render();
    expect(l.rows()).toEqual(['CDR', 'PKN', 'ALE']);
    expect(words(l.el.querySelector('tbody tr'))).toBe('dziś, 19:43 CDR Z limitem Kupno 250,00 0 / 10 Przyjęte 2 509,75 zł Anuluj Przebieg');
    expect(words(l.el.querySelectorAll('tbody tr')[1])).toContain('10 / 10 Wykonane —');
  });

  it('filters active and finished orders with counts', async () => {
    const l = await render();
    expect([...l.el.querySelectorAll('.chip')].map((c) => words(c))).toEqual(['Wszystkie 3', 'Aktywne 2', 'Zakończone 1']);
    await l.click('Aktywne');
    expect(l.rows()).toEqual(['CDR', 'ALE']);
    await l.click('Zakończone');
    expect(l.rows()).toEqual(['PKN']);
  });

  it('asks before cancelling and tells the parent when the exchange agreed', async () => {
    const l = await render();
    await l.click('Anuluj');
    expect(l.el.querySelector('.confirm')?.textContent).toContain('Anulować?');
    await l.click('Nie');
    expect(l.el.querySelector('.confirm')).toBeNull();
    backend.expectNone(() => true);

    await l.click('Anuluj');
    await l.click('Tak');
    const request = backend.expectOne(`/api/me/orders/${accepted.id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(anOrder({ status: 'cancelrequested' }), { status: 202, statusText: 'Accepted' });
    await l.stable();
    expect(l.changed).toHaveBeenCalledTimes(1);
  });

  it('explains a cancellation that came too late or got no answer', async () => {
    const l = await render();
    await l.click('Anuluj');
    await l.click('Tak');
    backend.expectOne(`/api/me/orders/${accepted.id}`).flush({ title: 'Order is not open' }, { status: 409, statusText: 'Conflict' });
    await l.stable();
    expect(l.el.querySelector('[role="alert"]')?.textContent).toContain('zdążyło się zakończyć');
    expect(l.changed).toHaveBeenCalledTimes(1);

    await l.click('Anuluj');
    await l.click('Tak');
    backend.expectOne(`/api/me/orders/${accepted.id}`).flush({ title: 'Exchange did not answer, try again' }, { status: 503, statusText: 'Service Unavailable' });
    await l.stable();
    expect(l.el.querySelector('[role="alert"]')?.textContent).toContain('Giełda nie odpowiedziała na anulowanie');
  });

  it('invites the first order when there are none', async () => {
    const l = await render([]);
    expect(l.el.querySelector('.empty')?.textContent).toContain('Nie masz jeszcze zleceń');
    expect(l.el.querySelector('bl-order-saga')).toBeNull();
  });
});
