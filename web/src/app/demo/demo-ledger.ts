import { HistoryEntryDto, PostingDto } from '../core/account/account-api';
import { unitsOf } from '../core/account/documents';
import { toApiAmount } from '../core/format/money';

/**
 * Ksiega trybu demo w pamieci przegladarki. Dokumenty maja te same zapisy co w domenie (JournalEntries):
 * wplata, wyplata, blokada, zwolnienie, kupno, sprzedaz. Kwoty: grosze dla PLN, sztuki dla akcji.
 * Salda nie sa trzymane osobno — liczy sie je z zapisow, jak w prawdziwej ksiedze.
 */

const PLN = 'PLN';

interface Line {
  readonly owner: PostingDto['owner'];
  readonly account: PostingDto['account'];
  readonly asset: string;
  readonly side: PostingDto['side'];
  readonly units: number;
}

const client = (account: 'available' | 'blocked', asset: string, side: Line['side'], units: number): Line =>
  ({ owner: 'client', account, asset, side, units });
const broker = (account: 'bank' | 'depository' | 'feeIncome', asset: string, side: Line['side'], units: number): Line =>
  ({ owner: 'broker', account, asset, side, units });

/** Zmiana salda konta klienta: konta klienta rosna po stronie Ma. */
function change(line: Line): number {
  return line.side === 'credit' ? line.units : -line.units;
}

function amount(asset: string, units: number): string {
  return asset === PLN ? toApiAmount(units) : String(units);
}

export class DemoLedger {
  private readonly entries: HistoryEntryDto[] = [];

  /** Dokumenty od najstarszych. */
  get all(): readonly HistoryEntryDto[] {
    return this.entries;
  }

  deposit(cents: number, at: number, key: string): HistoryEntryDto {
    return this.post('deposit', at, key, null, [broker('bank', PLN, 'debit', cents), client('available', PLN, 'credit', cents)]);
  }

  withdraw(cents: number, at: number, key: string): HistoryEntryDto {
    return this.post('withdrawal', at, key, null, [client('available', PLN, 'debit', cents), broker('bank', PLN, 'credit', cents)]);
  }

  block(asset: string, units: number, at: number, orderId: string): HistoryEntryDto {
    return this.post('block', at, `order:${orderId}:block`, orderId, [client('available', asset, 'debit', units), client('blocked', asset, 'credit', units)]);
  }

  release(asset: string, units: number, at: number, orderId: string): HistoryEntryDto {
    return this.post('release', at, `order:${orderId}:release`, orderId, [client('blocked', asset, 'debit', units), client('available', asset, 'credit', units)]);
  }

  purchase(symbol: string, quantity: number, value: number, fee: number, at: number, orderId: string, trade: number): HistoryEntryDto {
    const lines = [
      client('blocked', PLN, 'debit', value + fee),
      broker('bank', PLN, 'credit', value),
      broker('depository', symbol, 'debit', quantity),
      client('available', symbol, 'credit', quantity),
    ];
    if (fee > 0) {
      lines.push(broker('feeIncome', PLN, 'credit', fee));
    }
    return this.post('purchase', at, `order:${orderId}:trade:${trade}`, orderId, lines);
  }

  sale(symbol: string, quantity: number, value: number, fee: number, at: number, orderId: string, trade: number): HistoryEntryDto {
    const lines = [client('blocked', symbol, 'debit', quantity), broker('depository', symbol, 'credit', quantity), broker('bank', PLN, 'debit', value)];
    if (value > fee) {
      lines.push(client('available', PLN, 'credit', value - fee));
    }
    if (fee > 0) {
      lines.push(broker('feeIncome', PLN, 'credit', fee));
    }
    return this.post('sale', at, `order:${orderId}:trade:${trade}`, orderId, lines);
  }

  /** Saldo konta klienta z zapisow (do chwili <code>until</code>, domyslnie wszystkie). */
  balance(account: 'available' | 'blocked', asset: string, until = Infinity): number {
    let total = 0;
    for (const e of this.entries) {
      if (Date.parse(e.createdAt) > until) {
        break;
      }
      for (const p of e.postings) {
        if (p.owner === 'client' && p.account === account && p.asset === asset) {
          total += change({ ...p, units: unitsOf(p) });
        }
      }
    }
    return total;
  }

  /** Spolki, ktorych akcje kiedykolwiek byly na rachunku. */
  symbols(): string[] {
    return [...new Set(this.entries.flatMap((e) => e.postings.filter((p) => p.asset !== PLN && p.owner === 'client').map((p) => p.asset)))].sort();
  }

  private post(description: string, at: number, key: string, orderId: string | null, lines: Line[]): HistoryEntryDto {
    const sequence = this.entries.length + 1;
    const mine = lines.filter((l) => l.owner === 'client');
    const entry: HistoryEntryDto = {
      id: `demo-${sequence}`,
      sequence,
      description,
      createdAt: new Date(at).toISOString(),
      orderId,
      key,
      changes: mine.map((l) => ({
        account: l.account as 'available' | 'blocked',
        asset: l.asset,
        change: `${change(l) >= 0 ? '+' : '-'}${amount(l.asset, l.units)}`,
      })),
      postings: lines.map((l) => ({ owner: l.owner, account: l.account, asset: l.asset, side: l.side, amount: amount(l.asset, l.units) })),
    };
    this.entries.push(entry);
    return entry;
  }
}
