import { HistoryEntryDto, PostingDto } from '../../core/account/account-api';
import { tradeTitle, unitsOf, unitsText } from '../../core/account/documents';
import { warsawDay } from '../../core/format/dates';
import { shortId } from '../../core/format/ids';
import { formatCents, formatQuantity } from '../../core/format/money';

/**
 * Dokumenty ksiegi dla ludzi, bez Angulara. Kwoty liczymy na calkowitych groszach (zlotowki) i sztukach (akcje),
 * tak jak w ksiedze — suma Wn i suma Ma musza sie zgadzac co do grosza.
 */

export type DocTone = 'cash' | 'block' | 'release' | 'buy' | 'sell' | 'other';

const KINDS: Readonly<Record<string, { label: string; tone: DocTone }>> = {
  deposit: { label: 'Wpłata', tone: 'cash' },
  withdrawal: { label: 'Wypłata', tone: 'cash' },
  block: { label: 'Blokada', tone: 'block' },
  release: { label: 'Zwolnienie', tone: 'release' },
  purchase: { label: 'Kupno', tone: 'buy' },
  sale: { label: 'Sprzedaż', tone: 'sell' },
};

const ACCOUNTS: Readonly<Record<PostingDto['account'], string>> = {
  available: 'Dostępne',
  blocked: 'Zablokowane',
  bank: 'Bank',
  depository: 'Depozyt',
  feeIncome: 'Przychód z prowizji',
};

const PLN = 'PLN';

export interface PostingRow {
  readonly mine: boolean;
  readonly owner: string;
  readonly name: string;
  readonly asset: string;
  /** Kwota po stronie Wn albo pusty tekst. */
  readonly debit: string;
  readonly credit: string;
}

export interface DocView {
  readonly sequence: number;
  readonly kind: string;
  readonly tone: DocTone;
  readonly title: string;
  readonly amount: string;
  readonly key: string | null;
  readonly orderId: string | null;
  readonly createdAt: string;
  readonly rows: readonly PostingRow[];
  /** Bilans kazdego aktywa: "PLN 997,48 = 997,48". */
  readonly sums: readonly string[];
  /** Wn = Ma w kazdym aktywie. Nie, gdy czesc zapisow jest na kontach innych klientow (ich nie pokazujemy). */
  readonly balanced: boolean;
}

/** Wszystko, co ekran pokazuje o jednym dokumencie. */
export function docView(e: HistoryEntryDto): DocView {
  const kind = KINDS[e.description] ?? { label: e.description, tone: 'other' as const };
  const rows = e.postings.map((p) => ({
    mine: p.owner === 'client',
    owner: p.owner === 'client' ? 'Twoje' : 'Broker',
    name: ACCOUNTS[p.account] ?? p.account,
    asset: p.asset,
    debit: p.side === 'debit' ? unitsText(p.asset, unitsOf(p)) : '',
    credit: p.side === 'credit' ? unitsText(p.asset, unitsOf(p)) : '',
  }));
  const { sums, balanced } = balanceOf(e.postings);
  return {
    sequence: e.sequence,
    kind: kind.label,
    tone: kind.tone,
    title: titleOf(e),
    amount: headline(e.postings),
    key: e.key,
    orderId: e.orderId,
    createdAt: e.createdAt,
    rows,
    sums,
    balanced,
  };
}

function balanceOf(postings: readonly PostingDto[]): { sums: string[]; balanced: boolean } {
  const totals = new Map<string, { debit: number; credit: number }>();
  for (const p of postings) {
    const t = totals.get(p.asset) ?? { debit: 0, credit: 0 };
    t[p.side] += unitsOf(p);
    totals.set(p.asset, t);
  }
  const sums = [...totals].map(([asset, t]) => {
    const show = (units: number) => (asset === PLN ? formatCents(units) : formatQuantity(units));
    return `${asset} ${show(t.debit)} ${t.debit === t.credit ? '=' : '≠'} ${show(t.credit)}`;
  });
  return { sums, balanced: [...totals.values()].every((t) => t.debit === t.credit) };
}

/** Najwazniejsza kwota dokumentu: najwiekszy zapis klienta w zlotowkach, a bez zlotowek — akcje. */
function headline(postings: readonly PostingDto[]): string {
  const mine = postings.filter((p) => p.owner === 'client');
  const cash = mine.filter((p) => p.asset === PLN);
  const pick = (cash.length ? cash : mine).reduce<PostingDto | null>((best, p) => (!best || unitsOf(p) > unitsOf(best) ? p : best), null);
  if (!pick) {
    return '—';
  }
  return pick.asset === PLN ? `${formatCents(unitsOf(pick))} zł` : unitsText(pick.asset, unitsOf(pick));
}

function titleOf(e: HistoryEntryDto): string {
  const order = e.orderId ? ` · zlecenie ${shortId(e.orderId)}` : '';
  const shares = e.postings.find((p) => p.owner === 'client' && p.asset !== PLN);
  switch (e.description) {
    case 'deposit':
      return 'Przelew z banku';
    case 'withdrawal':
      return 'Przelew do banku';
    case 'block':
      return shares ? `Pod sprzedaż ${unitsText(shares.asset, unitsOf(shares))} ${shares.asset}${order}` : `Pod kupno${order}`;
    case 'release':
      return shares ? `Niesprzedane ${unitsText(shares.asset, unitsOf(shares))} ${shares.asset} wracają${order}` : `Reszta blokady wraca${order}`;
    case 'purchase':
    case 'sale':
      return tradeTitle(e.postings) ?? KINDS[e.description].label;
    default:
      return e.description;
  }
}

/** Czy dokument pasuje do wyszukiwania: numer zlecenia, klucz, walor albo opis. */
export function matchesSearch(doc: DocView, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) {
    return true;
  }
  const haystack = [doc.title, doc.kind, doc.key ?? '', doc.orderId ?? '', `#${doc.sequence}`, ...doc.rows.map((r) => r.asset)];
  return haystack.some((text) => text.toLowerCase().includes(q));
}

export interface DayGroup {
  readonly label: string;
  readonly docs: readonly DocView[];
}

/** Dokumenty po dniach (czas warszawski), najnowsze pierwsze: "Dziś · 16.10.2026", "Wczoraj · 15.10.2026". */
export function byDay(docs: readonly DocView[], now: Date): DayGroup[] {
  const today = warsawDay(now);
  const yesterday = warsawDay(new Date(now.getTime() - 86_400_000));
  const groups: { label: string; docs: DocView[] }[] = [];
  for (const doc of docs) {
    const day = warsawDay(new Date(doc.createdAt));
    const label = day === today ? `Dziś · ${day}` : day === yesterday ? `Wczoraj · ${day}` : day;
    const last = groups.at(-1);
    if (last?.label === label) {
      last.docs.push(doc);
    } else {
      groups.push({ label, docs: [doc] });
    }
  }
  return groups;
}
