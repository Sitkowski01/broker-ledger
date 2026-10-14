import { plural } from './plural';

describe('plural', () => {
  it.each([
    [0, '0 walorów'],
    [1, '1 walor'],
    [2, '2 walory'],
    [4, '4 walory'],
    [5, '5 walorów'],
    [11, '11 walorów'],
    [12, '12 walorów'],
    [14, '14 walorów'],
    [21, '21 walorów'],
    [22, '22 walory'],
    [111, '111 walorów'],
    [112, '112 walorów'],
    [122, '122 walory'],
  ])('%i -> %s', (n, text) => {
    expect(plural(n, 'walor', 'walory', 'walorów')).toBe(text);
  });
});
