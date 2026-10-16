import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { toApiAmount } from '../format/money';

/** Kwoty z API sa tekstem ("8214.36") — patrz core/format/money.ts. */
export interface PositionDto {
  readonly symbol: string;
  readonly quantity: number;
  readonly averageCost: string;
  readonly costBasis: string;
  readonly lastPrice: string | null;
  readonly priceAt: string | null;
  readonly marketValue: string;
  readonly unrealizedPnl: string;
  readonly returnPercent: string | null;
  readonly realizedPnl: string;
}

export interface PortfolioDto {
  readonly positions: readonly PositionDto[];
  readonly closed: readonly { readonly symbol: string; readonly realizedPnl: string }[];
  readonly summary: {
    /** Gotowka razem: wolna i zablokowana pod zlecenia. */
    readonly cash: string;
    readonly sharesValue: string;
    readonly costBasis: string;
    readonly unrealizedPnl: string;
    readonly realizedPnl: string;
    readonly totalValue: string;
  };
  /** Wartosc rachunku w innych walutach po kursie sredni NBP; pusta, gdy NBP nie odpowiada od startu. */
  readonly totalValueIn: readonly { readonly currency: string; readonly amount: string }[];
  readonly rates: {
    readonly source: 'NBP';
    readonly table: string;
    readonly effectiveDate: string;
    readonly stale: boolean;
    readonly mid: Readonly<Record<string, string>>;
  } | null;
}

export interface BalanceDto {
  readonly asset: string;
  readonly kind: 'Cash' | 'Shares';
  readonly available: string;
  readonly blocked: string;
}

export interface HistoryEntryDto {
  readonly id: string;
  readonly sequence: number;
  readonly description: string;
  readonly createdAt: string;
  readonly orderId: string | null;
  /** Klucz idempotencji dokumentu: "order:{id}:block", "order:{id}:trade:...", przy przelewie klucz klienta. */
  readonly key: string | null;
  readonly changes: readonly { readonly account: 'available' | 'blocked'; readonly asset: string; readonly change: string }[];
  /** Caly dokument po ksiegowemu: konta klienta i brokera, strona Wn/Ma. */
  readonly postings: readonly PostingDto[];
}

export interface PostingDto {
  readonly owner: 'client' | 'broker';
  readonly account: 'available' | 'blocked' | 'bank' | 'depository' | 'feeIncome';
  readonly asset: string;
  readonly side: 'debit' | 'credit';
  readonly amount: string;
}

export type HistoryKind = 'transfers' | 'trades' | 'blocks';

export interface HistoryPageDto {
  readonly items: readonly HistoryEntryDto[];
  readonly nextBefore: number | null;
}

/** Wynik rachunku w chwili: wartosc, wplacone netto i ich roznica (kwoty jak wszedzie — tekst). */
export interface PerformancePointDto {
  readonly at: string;
  readonly value: string;
  readonly invested: string;
  readonly result: string;
}

export type TransferKind = 'deposit' | 'withdrawal';

export const ACCOUNT_URLS = {
  portfolio: '/api/me/portfolio',
  performance: '/api/me/performance',
  balances: '/api/me/balances',
  history: '/api/me/history',
} as const;

const TRANSFER_URLS: Readonly<Record<TransferKind, string>> = {
  deposit: '/api/me/deposits',
  withdrawal: '/api/me/withdrawals',
};

/**
 * Przelewy na rachunku. Klucz idempotencji wybiera wolajacy: ten sam klucz przy ponowieniu
 * (np. po zerwanym polaczeniu) nie zaksieguje przelewu drugi raz.
 */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  /** Strona historii od najnowszych; <code>before</code> to kursor z poprzedniej strony (nextBefore). */
  history(limit: number, before: number | null = null, kind: HistoryKind | null = null): Observable<HistoryPageDto> {
    const params: Record<string, string | number> = { limit };
    if (before !== null) {
      params['before'] = before;
    }
    if (kind !== null) {
      params['kind'] = kind;
    }
    return this.http.get<HistoryPageDto>(ACCOUNT_URLS.history, { params });
  }

  transfer(kind: TransferKind, cents: number, idempotencyKey: string): Observable<unknown> {
    return this.http.post(
      TRANSFER_URLS[kind],
      { amount: toApiAmount(cents), currency: 'PLN' },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }
}
