import { newIdempotencyKey } from './idempotency-key';

describe('newIdempotencyKey', () => {
  afterEach(() => vi.restoreAllMocks());

  /** Podstawia stale "losowe" bajty, zeby sprawdzic bity wersji i wariantu. */
  function randomBytes(value: number) {
    vi.spyOn(crypto, 'getRandomValues').mockImplementation((array) => {
      (array as Uint8Array).fill(value);
      return array;
    });
  }

  it('is a version 4 UUID that the API accepts as a key', () => {
    const key = newIdempotencyKey();

    expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    // API: 1-100 drukowalnych znakow ASCII.
    expect(key.length).toBeLessThanOrEqual(100);
  });

  it('sets the version and variant bits whatever the random bytes are', () => {
    randomBytes(0xff);
    expect(newIdempotencyKey()).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff');

    randomBytes(0);
    expect(newIdempotencyKey()).toBe('00000000-0000-4000-8000-000000000000');
  });

  it('does not repeat', () => {
    const keys = new Set(Array.from({ length: 1000 }, newIdempotencyKey));
    expect(keys.size).toBe(1000);
  });
});
