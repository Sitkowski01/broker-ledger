import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StreamStatus } from '../core/events/client-events';

export interface TickerItem {
  readonly label: string;
  readonly value: string;
  /** Kierunek ostatniej zmiany albo zrodlo kursu (np. "NBP"). */
  readonly mark: '▲' | '▼' | string;
}

/** Pasek notowan u gory ekranu, jak w portfolio. Pusty — nie zajmuje miejsca. */
@Component({
  selector: 'bl-ticker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ticker.html',
  styleUrl: './ticker.css',
  host: { '[hidden]': '!items().length' },
})
export class Ticker {
  readonly items = input<readonly TickerItem[]>([]);
  /** Stan strumienia zdarzen: "Live" tylko wtedy, gdy naprawde slyszymy serwer. */
  readonly status = input<StreamStatus>('live');

  /** Lista powtorzona tyle razy, zeby przesuniecie o polowe szerokosci dalo plynna petle. */
  protected readonly tape = computed(() => {
    const items = this.items();
    const copies = Math.max(2, Math.ceil(16 / Math.max(items.length, 1)) * 2);
    return Array.from({ length: copies }, () => items).flat();
  });

  protected readonly summary = computed(() =>
    this.items().map((i) => `${i.label} ${i.value}`).join(', '),
  );

  protected markClass(mark: string): string {
    return mark === '▲' ? 'up' : mark === '▼' ? 'down' : 'note';
  }
}
