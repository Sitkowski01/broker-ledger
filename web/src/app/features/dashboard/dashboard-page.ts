import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ACCOUNT_URLS, BalanceDto, HistoryPageDto, PerformancePointDto, PortfolioDto } from '../../core/account/account-api';
import { cashOf } from '../../core/account/balances';
import { toApiError } from '../../core/api/api-error';
import { lastGood, loadErrorOf } from '../../core/api/last-good';
import { batched, ClientEvents, SLOW_EVENT_BATCH_MS } from '../../core/events/client-events';
import { longDate } from '../../core/format/dates';
import { PageHeader } from '../../shared/page-header';
import { SkeletonPanel } from '../../shared/skeleton-panel';
import { AccountValue } from './account-value';
import { PortfolioTable } from './portfolio-table';
import { ResultChart } from './result-chart';
import { SettlementFeed } from './settlement-feed';
import { TransferPanel } from './transfer-panel';
import { recentTransfers } from './transfer-model';

/** Ile ostatnich przelewow pokazuje panel (API filtruje po rodzaju dokumentu). */
const RECENT_TRANSFERS = 3;
/** Ile ostatnich dokumentow przegladamy dla panelu Rozliczenia. */
const FEED_WINDOW = 20;
/** Co ile odswiezamy "dzis" w naglowku i przy godzinach — Pulpit bywa otwarty przez noc. */
export const CLOCK_TICK_MS = 60_000;

/** Pulpit: wartosc rachunku (takze w walutach NBP), przelewy i otwarte pozycje. */
@Component({
  selector: 'bl-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, AccountValue, ResultChart, TransferPanel, PortfolioTable, SettlementFeed, SkeletonPanel],
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
    params: { limit: RECENT_TRANSFERS, kind: 'transfers' },
  }));
  protected readonly days = signal(30);
  private readonly performanceResource = httpResource<{ points: PerformancePointDto[] }>(() => ({
    url: ACCOUNT_URLS.performance,
    params: { days: this.days() },
  }));
  protected readonly performance = lastGood(this.performanceResource);
  /** Ostatnie dokumenty do panelu Rozliczenia (przelewy model odfiltruje). */
  private readonly feedResource = httpResource<HistoryPageDto>(() => ({ url: ACCOUNT_URLS.history, params: { limit: FEED_WINDOW } }));

  protected readonly portfolio = lastGood(this.portfolioResource);
  private readonly balances = lastGood(this.balancesResource);
  private readonly history = lastGood(this.historyResource);
  protected readonly feed = lastGood(this.feedResource);

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
    // Przelew w drugiej karcie, transakcja z gieldy, zwolniona blokada — kazda zmiana salda odswieza Pulpit.
    inject(ClientEvents)
      .events$.pipe(batched(SLOW_EVENT_BATCH_MS), takeUntilDestroyed())
      .subscribe(() => this.reload());
    const timer = setInterval(() => this.now.set(new Date()), CLOCK_TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected reload(): void {
    this.portfolioResource.reload();
    this.balancesResource.reload();
    this.historyResource.reload();
    this.feedResource.reload();
    this.performanceResource.reload();
  }
}
