import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { ClientEvents } from '../core/events/client-events';
import { timeOf } from '../core/format/dates';

/**
 * Pasek "Brak polaczenia" — tylko gdy juz kiedys slyszelismy serwer (pierwsze laczenie to nie awaria).
 * Mowi, z kiedy sa dane na ekranie, i ze nic nie przepada: zdarzenia gieldy czekaja w Kafce.
 */
@Component({
  selector: 'bl-connection-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div class="banner" role="alert">
        <span class="icon" aria-hidden="true">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M2 8.8a15 15 0 0 1 4.2-2.6M10.7 5.1A15 15 0 0 1 22 8.8M5 12.5a10 10 0 0 1 3.4-2M15.3 10.6A10 10 0 0 1 19 12.5M8.5 16a5 5 0 0 1 7 0M12 20h.01M3 3l18 18" /></svg>
        </span>
        <p>
          <strong>Brak połączenia z serwerem.</strong>
          Pokazujemy dane z <span class="num">{{ since() }}</span>. Nic nie przepada — zdarzenia z giełdy czekają w Kafce
          i rozliczą się po powrocie połączenia.
        </p>
        <span class="actions">
          <button type="button" class="ghost" (click)="events.reconnect()">Spróbuj teraz</button>
          @if (countdown(); as seconds) {
            <span class="num auto">auto za {{ seconds }} s</span>
          }
        </span>
      </div>
    }
  `,
  styles: `
    .banner {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px 16px;
      max-width: 1360px;
      margin: 0 auto 24px;
      padding: 14px 16px;
      border: 1px solid rgba(239, 68, 68, 0.35);
      border-radius: var(--radius-sm);
      background: var(--bear-soft);
      font-size: 14px;
      color: #fecaca;
    }
    .icon {
      display: inline-grid;
      place-items: center;
      flex: none;
      width: 40px;
      height: 40px;
      border-radius: 50%;
      border: 1px solid rgba(239, 68, 68, 0.35);
      color: var(--bear-text);
    }
    p {
      flex: 1 1 320px;
    }
    strong {
      color: #fff;
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .auto {
      font-size: 13px;
      color: var(--text-2);
    }
  `,
})
export class ConnectionBanner {
  protected readonly events = inject(ClientEvents);
  private readonly now = signal(Date.now());

  protected readonly visible = computed(() => this.events.status() === 'offline' && this.events.lastHeardAt() !== null);
  protected readonly since = computed(() => {
    const at = this.events.lastHeardAt();
    return at ? timeOf(at.toISOString()) : '';
  });
  protected readonly countdown = computed(() => {
    const at = this.events.nextAttemptAt();
    return at === null ? null : Math.max(1, Math.ceil((at - this.now()) / 1_000));
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
