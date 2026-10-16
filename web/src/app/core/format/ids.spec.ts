import { shortId } from './ids';

describe('shortId', () => {
  it('takes the random end of a UUID, not the timestamp at its start', () => {
    expect(shortId('01a1459f-e3ad-742c-b1d2-0123456789ab')).toBe('456789ab');
    expect(shortId('01a1459f-e3ad-742c-b1d2-0123456789ab', 4)).toBe('89ab');
  });
});
