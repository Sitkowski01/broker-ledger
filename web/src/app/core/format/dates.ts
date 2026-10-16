/** Daty po polsku i w czasie warszawskim — tak liczy gielda, niezaleznie od strefy przegladarki. */

const WARSAW = 'Europe/Warsaw';

// Formatery buduje sie raz: Intl.DateTimeFormat jest drogi w tworzeniu, a tabela formatuje date w kazdym wierszu.
const DAY = new Intl.DateTimeFormat('pl-PL', { timeZone: WARSAW, day: '2-digit', month: '2-digit', year: 'numeric' });
const DAY_TIME = new Intl.DateTimeFormat('pl-PL', {
  timeZone: WARSAW,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});
const WEEKDAY = new Intl.DateTimeFormat('pl-PL', { timeZone: WARSAW, weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' });

function parts(format: Intl.DateTimeFormat, date: Date): Record<string, string> {
  return Object.fromEntries(format.formatToParts(date).map((p) => [p.type, p.value]));
}

/** "dziś, 18:05" albo "14.09, 09:12". */
export function shortDateTime(iso: string, now: Date): string {
  const at = parts(DAY_TIME, new Date(iso));
  const today = parts(DAY, now);
  const sameDay = at['day'] === today['day'] && at['month'] === today['month'] && at['year'] === today['year'];
  return `${sameDay ? 'dziś' : `${at['day']}.${at['month']}`}, ${at['hour']}:${at['minute']}`;
}

/** "wtorek 13.10.2026" do naglowka ekranu. */
export function longDate(now: Date): string {
  const p = parts(WEEKDAY, now);
  return `${p['weekday']} ${p['day']}.${p['month']}.${p['year']}`;
}

/** Dzien w Warszawie jako "16.10.2026" — do grupowania po dniach i porownan "dzis". */
export function warsawDay(date: Date): string {
  const p = parts(DAY, date);
  return `${p['day']}.${p['month']}.${p['year']}`;
}

const TIME = new Intl.DateTimeFormat('pl-PL', { timeZone: WARSAW, hour: '2-digit', minute: '2-digit', second: '2-digit' });

/** Godzina z sekundami w Warszawie: "19:42:07" — dokumenty z tej samej minuty da sie rozroznic. */
export function timeOf(iso: string): string {
  return TIME.format(new Date(iso));
}
