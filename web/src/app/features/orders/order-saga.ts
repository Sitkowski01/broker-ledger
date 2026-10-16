import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { shortDateTime } from '../../core/format/dates';
import { shortId } from '../../core/format/ids';
import { OrderDto } from '../../core/orders/order-api';
import { orderTitle, sagaNote, sagaSteps } from './order-model';

/** Os przebiegu jednego zlecenia: zapis → blokada → gielda → rozliczenie → zwolnienie reszty. */
@Component({
  selector: 'bl-order-saga',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h3>{{ title() }}</h3>
      <span class="num ids">{{ ids() }}</span>
    </div>
    <ol class="steps">
      @for (s of steps(); track s.name; let i = $index) {
        <li [class]="s.state">
          <span class="node-row">
            <span class="node num" aria-hidden="true">
              @switch (s.state) {
                @case ('done') {
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M5 12l5 5L20 7" /></svg>
                }
                @case ('fail') {
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>
                }
                @default {
                  {{ i + 1 }}
                }
              }
            </span>
            <span class="ln" aria-hidden="true"></span>
          </span>
          <span class="txt">
            <span class="name">{{ s.name }}<span class="sr-only"> — {{ stateText[s.state] }}</span></span>
            <span class="num note">{{ s.note }}</span>
          </span>
        </li>
      }
    </ol>
    @if (note(); as note) {
      <p class="foot">{{ note }}</p>
    }
  `,
  styleUrl: './order-saga.css',
})
export class OrderSaga {
  readonly order = input.required<OrderDto>();
  readonly now = input.required<Date>();

  protected readonly stateText = { done: 'zrobione', now: 'w toku', todo: 'jeszcze nie', fail: 'nie powiodło się' } as const;
  protected readonly title = computed(() => orderTitle(this.order()));
  protected readonly ids = computed(() => {
    const o = this.order();
    return `zlecenie ${shortId(o.id)} · nr giełdy ${o.exchangeOrderId ?? '—'}`;
  });
  protected readonly steps = computed(() => {
    const now = this.now();
    return sagaSteps(this.order(), (iso) => shortDateTime(iso, now));
  });
  protected readonly note = computed(() => sagaNote(this.order()));
}
