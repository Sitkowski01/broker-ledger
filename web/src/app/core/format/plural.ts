/** Liczba z odmienionym rzeczownikiem: 1 walor, 2-4 walory (ale 12-14 walorow), 5+ walorow. */
export function plural(n: number, one: string, few: string, many: string): string {
  const lastTwo = n % 100;
  const last = n % 10;
  const form = n === 1 ? one : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? few : many;
  return `${n} ${form}`;
}
