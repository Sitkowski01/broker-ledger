import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PortfolioDto } from '../../core/account/account-api';
import { formatPrice, formatQuantity, formatSigned, parseCents } from '../../core/format/money';
import { companyName } from '../../core/market/companies';
import { shortDateTime } from '../../core/format/dates';
import { plural } from '../../core/format/plural';
import { formatBasisPoints, returnOn, shareOfTotal, trendOf } from './account-model';

/** Otwarte pozycje: ilosc, srednia cena zakupu, ostatnia transakcja na gieldzie i wynik. */
@Component({
  selector: 'bl-portfolio-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './portfolio-table.html',
  styleUrl: './portfolio-table.css',
  host: { class: 'panel', role: 'region', 'aria-labelledby': 'portfolio-title' },
})
export class PortfolioTable {
  readonly portfolio = input.required<PortfolioDto>();
  /** Zegar Pulpitu — od niego zalezy, czy transakcja byla "dzis". */
  readonly now = input(new Date());

  protected readonly rows = computed(() => {
    const total = parseCents(this.portfolio().summary.sharesValue) ?? 0;
    const now = this.now();
    return this.portfolio().positions.map((p) => {
      const share = shareOfTotal(parseCents(p.marketValue) ?? 0, total);
      const percent = p.returnPercent === null ? null : parseCents(p.returnPercent);
      return {
        symbol: p.symbol,
        name: companyName(p.symbol),
        quantity: formatQuantity(p.quantity),
        average: formatPrice(p.averageCost),
        last: p.lastPrice === null ? '—' : formatPrice(p.lastPrice),
        at: p.priceAt === null ? 'brak transakcji' : shortDateTime(p.priceAt, now),
        value: formatPrice(p.marketValue),
        share: share.percent,
        width: share.width,
        pnl: formatSigned(p.unrealizedPnl),
        percent: percent === null ? '' : formatBasisPoints(percent),
        trend: trendOf(p.unrealizedPnl),
      };
    });
  });

  protected readonly count = computed(() => plural(this.rows().length, 'walor', 'walory', 'walorów'));

  protected readonly total = computed(() => {
    const s = this.portfolio().summary;
    return {
      cost: formatPrice(s.costBasis),
      value: formatPrice(s.sharesValue),
      pnl: formatSigned(s.unrealizedPnl),
      percent: returnOn(parseCents(s.unrealizedPnl) ?? 0, parseCents(s.costBasis) ?? 0) ?? '',
      trend: trendOf(s.unrealizedPnl),
    };
  });

  protected arrow(trend: string): string {
    return trend === 'up' ? '▲' : trend === 'down' ? '▼' : '';
  }
}
