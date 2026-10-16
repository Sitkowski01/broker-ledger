import { HttpClient, HttpErrorResponse, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { demoInterceptor } from './demo-backend';

describe('demo interceptor', () => {
  function http() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([demoInterceptor]))] });
    return TestBed.inject(HttpClient);
  }

  it('answers the API like the server, without a network', async () => {
    const client = http();
    const login = await firstValueFrom(client.post<{ accessToken: string }>('/api/auth/login', { email: 'a@b.pl', password: 'x' }));
    expect(login.accessToken).toBe('demo');

    const page = await firstValueFrom(client.get<{ items: unknown[] }>('/api/me/history', { params: { limit: 2, kind: 'transfers' } }));
    expect(page.items).toHaveLength(1);
    const orders = await firstValueFrom(client.get<{ id: string }[]>('/api/me/orders', { params: { limit: 50 } }));
    expect(orders.length).toBeGreaterThan(0);
    expect((await firstValueFrom(client.get<{ id: string }>(`/api/me/orders/${orders[0].id}`))).id).toBe(orders[0].id);
    expect((await firstValueFrom(client.get<{ points: unknown[] }>('/api/me/performance', { params: { days: 7 } }))).points.length).toBeGreaterThan(1);
    expect(await firstValueFrom(client.get<unknown[]>('/api/market/quotes'))).toHaveLength(6);
  });

  it('turns refusals into HTTP errors the screens already understand', async () => {
    const client = http();
    const headers = new HttpHeaders({ 'Idempotency-Key': 'w' });
    const error = await firstValueFrom(client.post('/api/me/withdrawals', { amount: '999999.00', currency: 'PLN' }, { headers })).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect([(error as HttpErrorResponse).status, (error as HttpErrorResponse).error.title]).toEqual([422, 'Insufficient funds']);

    const deposit = await firstValueFrom(client.post<{ outcome: string }>('/api/me/deposits', { amount: '100.00', currency: 'PLN' }, { headers }));
    expect(deposit.outcome).toBe('posted');
    const missing = await firstValueFrom(client.get('/api/me/orders/none')).catch((e: unknown) => e);
    expect((missing as HttpErrorResponse).status).toBe(404);
    const unknown = await firstValueFrom(client.get('/api/admin')).catch((e: unknown) => e);
    expect((unknown as HttpErrorResponse).status).toBe(404);
  });
});
