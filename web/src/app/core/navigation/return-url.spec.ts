import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/', '/zlecenia', '/ksiega?dzien=2026-10-13'])('keeps the in-app path %s', (path) => {
    expect(safeReturnUrl(path)).toBe(path);
  });

  it.each([null, undefined, '', 'zlecenia', '//evil.example', '/\\evil.example', 'https://evil.example', 'javascript:alert(1)'])(
    'sends %s back to the dashboard',
    (path) => {
      expect(safeReturnUrl(path)).toBe('/');
    },
  );
});
