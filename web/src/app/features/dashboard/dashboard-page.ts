import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ACCOUNT_URLS, BalanceDto, HistoryPageDto, PortfolioDto } from '../../core/account/account-api';
import { cashOf } from '../../core/account/balances';
import { toApiError } from '../../core/api/api-error';
import { lastGood, loadErrorOf } from '../../core/api/last-good';
import { longDate } from '../../core/format/dates';
import { PageHeader } from '../../shared/page-header';
import { AccountValue } from './account-value';
import { PortfolioTable } from './portfolio-table';
import { TransferPanel } from './transfer-panel';
import { recentTransfers } from './transfer-model';

/** Ile ostatnich dokumentow ksiegi przegladamy w poszukiwaniu przelewow. */
const HISTORY_WINDOW = 50;
/** Co ile odswiezamy "dzis" w naglowku i przy godzinach — Pulpit bywa otwarty przez noc. */
export const CLOCK_TICK_MS = 60_000;

/** Pulpit: wartosc rachunku (takze w walutach NBP), przelewy i otwarte pozycje. */
@Component({
  selector: 'bl-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, AccountValue, TransferPanel, PortfolioTable],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.css',
})
export class DashboardPage {
  protected readonly now = signal(new Date());
  protected readonly today = computed(() => longDate(this.now()));

  private readonly portfolioResource = httpResource<PortfolioDto>(() => ACCOUNT_URLS.portfolio);
  private readonly balancesResource = httpResource<BalanceDto[]>(() => ACCOUNT_URLS.balances);
  private readonly historyResource = httpResource<HistoryPageDto>(() => ({
    url: ACCOUNT_URLS.history,
    params: { limit: HISTORY_WINDOW },
  }));

  protected readonly portfolio = lastGood(this.portfolioResource);
  private readonly balances = lastGood(this.balancesResource);
  private readonly history = lastGood(this.historyResource);

  protected readonly ready = computed(() => this.portfolio() !== undefined && this.balances() !== undefined);
  protected readonly cash = computed(() => cashOf(this.balances() ?? []));
  /** Bez historii Pulpit dziala dalej — lista ostatnich przelewow jest po prostu pusta. */
  protected readonly recent = computed(() => recentTransfers(this.history()?.items ?? [], this.now()));
  protected readonly stale = computed(() => this.portfolio()?.rates?.stale ?? false);
  protected readonly loadError = computed(() => {
    const error = this.portfolioResource.error() ?? this.balancesResource.error();
    if (!error) {
      return null;
    }
    const api = toApiError(loadErrorOf(error));
    return 'message' in api ? api.message : 'Nie udało się wczytać rachunku.';
  });

  constructor() {
    const timer = setInterval(() => this.now.set(new Date()), CLOCK_TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected reload(): void {
    this.portfolioResource.reload();
    this.balancesResource.reload();
    this.historyResource.reload();
  }
}
