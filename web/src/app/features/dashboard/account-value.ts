import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { PortfolioDto } from '../../core/account/account-api';
import { formatSigned, parseCents } from '../../core/format/money';
import { accountStructure, currenciesOf, returnOn, trendOf, valueIn } from './account-model';

/** Wartosc rachunku w wybranej walucie, wynik na akcjach i z czego sklada sie rachunek. */
@Component({
  selector: 'bl-account-value',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-value.html',
  styleUrl: './account-value.css',
  host: { class: 'panel neon', role: 'region', 'aria-labelledby': 'value-title' },
})
export class AccountValue {
  private readonly document = inject(DOCUMENT);

  readonly portfolio = input.required<PortfolioDto>();
  readonly available = input.required<number>();
  readonly blocked = input.required<number>();

  private readonly picked = signal('PLN');

  protected readonly currencies = computed(() => currenciesOf(this.portfolio()));
  /** Wybrana waluta znika z listy, gdy NBP przestal odpowiadac — wtedy wracamy do zlotowek. */
  protected readonly code = computed(() => (this.currencies().includes(this.picked()) ? this.picked() : 'PLN'));
  protected readonly value = computed(() => valueIn(this.portfolio(), this.code()));
  protected readonly label = computed(() => {
    const v = this.value();
    return `${v.approx ? 'około ' : ''}${v.whole}${v.fraction === null ? '' : ',' + v.fraction} ${v.code}`;
  });

  protected readonly pnl = computed(() => {
    const summary = this.portfolio().summary;
    const trend = trendOf(summary.unrealizedPnl);
    return {
      trend,
      text: `${formatSigned(summary.unrealizedPnl)} zł`,
      percent: returnOn(parseCents(summary.unrealizedPnl) ?? 0, parseCents(summary.costBasis) ?? 0),
    };
  });

  protected readonly slices = computed(() =>
    accountStructure(parseCents(this.portfolio().summary.sharesValue) ?? 0, this.available(), this.blocked()),
  );
  protected readonly structureLabel = computed(
    () => 'Struktura rachunku: ' + this.slices().map((s) => `${s.label.toLowerCase()} ${s.percent}`).join(', '),
  );

  /** Nic na rachunku i nic w historii akcji — zamiast zer pokazujemy, od czego zaczac. */
  protected readonly empty = computed(() => {
    const p = this.portfolio();
    return (parseCents(p.summary.totalValue) ?? 0) === 0 && p.positions.length === 0 && p.closed.length === 0;
  });

  /** Przycisk z pustego rachunku prowadzi do pola kwoty w panelu przelewu obok. */
  protected startDeposit(): void {
    const amount = this.document.getElementById('amount');
    amount?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    amount?.focus({ preventScroll: true });
  }

  protected pick(code: string): void {
    this.picked.set(code);
  }
}
