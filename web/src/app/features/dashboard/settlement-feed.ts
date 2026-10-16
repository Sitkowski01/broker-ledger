import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HistoryEntryDto } from '../../core/account/account-api';
import { ClientEvents } from '../../core/events/client-events';
import { settlementFeed } from './settlement-feed-model';

/** Rozliczenia na zywo: transakcje, blokady i zwroty z ksiegi, odswiezane po sygnale z serwera. */
@Component({
  selector: 'bl-settlement-feed',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="bar">
      <h2 id="feed-title">Rozliczenia</h2>
      <span class="meta" [class.on]="live()"><span class="dot" [class.blink]="live()" aria-hidden="true"></span>{{ live() ? 'Na żywo' : 'Bez połączenia' }}</span>
    </div>
    @if (items().length) {
      <ol aria-live="polite">
        @for (f of items(); track f.sequence; let first = $first; let last = $last) {
          <li [class]="'tone-' + f.tone" [class.fresh]="first">
            <div class="when"><span class="num">{{ f.time }}</span><span class="micro status">{{ f.status }}</span></div>
            <span class="rail" aria-hidden="true"><span class="above" [class.none]="first"></span><span class="node"></span><span class="below" [class.none]="last"></span></span>
            <div class="what"><span class="title">{{ f.title }}</span><span class="detail">{{ f.detail }}</span></div>
          </li>
        }
      </ol>
    } @else {
      <p class="empty">Tu pojawią się blokady i transakcje Twoich zleceń, gdy tylko giełda je rozliczy.</p>
    }
    <div class="panel-foot foot">
      <span class="micro">Źródło: Kafka</span>
      <a routerLink="/ksiega">Dokumenty w księdze →</a>
    </div>
  `,
  styleUrl: './settlement-feed.css',
  host: { class: 'panel', role: 'region', 'aria-labelledby': 'feed-title' },
})
export class SettlementFeed {
  private readonly events = inject(ClientEvents);

  readonly entries = input.required<readonly HistoryEntryDto[]>();

  protected readonly live = computed(() => this.events.status() === 'live');
  protected readonly items = computed(() => settlementFeed(this.entries()));
}
