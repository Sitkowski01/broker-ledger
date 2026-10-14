import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { toApiError } from './api-error';

const http = (status: number, error: unknown = null, headers = new HttpHeaders()) =>
  new HttpErrorResponse({ status, error, headers });

describe('toApiError', () => {
  it('maps server field errors to Polish messages', () => {
    const error = toApiError(
      http(400, { errors: { password: ['password must be 8-128 characters'], 'Idempotency-Key': ['header required'] } }),
    );

    expect(error).toEqual({
      kind: 'validation',
      fields: { password: 'Hasło musi mieć od 8 do 128 znaków.', 'Idempotency-Key': 'header required' },
    });
  });

  it('reads the wait time of a rate limit', () => {
    const error = toApiError(http(429, null, new HttpHeaders({ 'Retry-After': '23' })));

    expect(error).toMatchObject({ kind: 'rate-limited', retryAfterSeconds: 23, message: 'Za dużo prób. Spróbuj ponownie za 23 s.' });
    expect(toApiError(http(429))).toMatchObject({ retryAfterSeconds: 60 });
  });

  it.each([
    [0, 'offline'],
    [401, 'unauthorized'],
    [404, 'not-found'],
    [409, 'conflict'],
    [500, 'server'],
    [503, 'server'],
  ])('status %i is %s', (status, kind) => {
    expect(toApiError(http(status)).kind).toBe(kind);
  });

  it('tells how much is left when the ledger refuses to go below zero', () => {
    expect(toApiError(http(422, { title: 'Insufficient funds', asset: 'PLN', available: '8214.36' }))).toEqual({
      kind: 'insufficient',
      message: 'Za mało wolnych środków. Dostępne: 8 214,36 zł.',
    });
    const short = (body: object) => toApiError(http(422, { title: 'Insufficient funds', ...body }));
    expect(short({ asset: 'CDR', available: '1200' })).toMatchObject({ message: 'Za mało akcji CDR. Dostępne: 1 200 szt.' });
    expect(short({ asset: 'CDR', available: '1.5' })).toMatchObject({ message: 'Za mało akcji CDR. Dostępne: 1.5 szt.' });
    expect(short({ asset: 'PLN' })).toMatchObject({ message: 'Za mało wolnych środków.' });
    expect(short({})).toMatchObject({ kind: 'insufficient', message: 'Za mało wolnych środków.' });
  });

  it('does not call a rejected order a lack of money', () => {
    // POST /api/orders odpowiada 422 z samym zleceniem, gdy gielda je odrzuci.
    expect(toApiError(http(422, { id: 'o1', status: 'Rejected', reason: 'market closed' }))).toEqual({ kind: 'rejected', message: 'Operacja odrzucona.' });
    expect(toApiError(http(422, null))).toMatchObject({ kind: 'rejected' });
  });

  it('keeps the title of a conflict and survives non-HTTP errors', () => {
    expect(toApiError(http(409, { title: 'E-mail already registered' }))).toMatchObject({ message: 'E-mail already registered' });
    expect(toApiError(new Error('boom'))).toMatchObject({ kind: 'server' });
    expect(toApiError(http(400, null))).toEqual({ kind: 'validation', fields: {} });
  });
});
