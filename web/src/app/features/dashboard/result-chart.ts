import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { PerformancePointDto } from '../../core/account/account-api';
import { chartGeometry } from './result-chart-model';

/** Zakresy wykresu: tydzien i miesiac, jak w API. */
export const RANGES = [
  { days: 7, label: '7 dni' },
  { days: 30, label: 'Miesiąc' },
] as const;

/** Wynik rachunku bez wplat i wyplat: punkt na koniec kazdego dnia i "teraz". Dane i odswiezanie daje Pulpit. */
@Component({
  selector: 'bl-result-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './result-chart.html',
  styleUrl: './result-chart.css',
})
export class ResultChart {
  /** undefined = jeszcze nie wczytane (wtedy nic nie pokazujemy). */
  readonly points = input.required<readonly PerformancePointDto[] | undefined>();
  readonly now = input.required<Date>();
  readonly days = model<number>(30);

  protected readonly ranges = RANGES;
  protected readonly chart = computed(() => chartGeometry(this.points() ?? [], this.now()));
  protected readonly loaded = computed(() => this.points() !== undefined);
}
