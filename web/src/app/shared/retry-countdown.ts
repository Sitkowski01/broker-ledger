import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';

const RING = 2 * Math.PI * 15;

/**
 * Serwer odpowiedzial 429: pierscien odlicza sekundy z naglowka Retry-After, a formularz zostaje wypelniony.
 * Po zera komponent mowi rodzicowi, ze mozna probowac dalej.
 */
@Component({
  selector: 'bl-retry-countdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg width="36" height="36" viewBox="0 0 36 36" aria-hidden="true">
      <circle cx="18" cy="18" r="15" class="track" />
      <circle cx="18" cy="18" r="15" class="ring" [attr.stroke-dasharray]="ring" [attr.stroke-dashoffset]="offset()" />
      <text x="18" y="22" text-anchor="middle" class="num">{{ left() }}</text>
    </svg>
    <span class="text">
      <strong>Za dużo zapytań w krótkim czasie</strong>
      <span>Kolejna próba za {{ left() }} s — formularz zostaje wypełniony.</span>
    </span>
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 12px 14px;
      border: 1px solid rgba(245, 183, 74, 0.3);
      border-radius: var(--radius-sm);
      background: rgba(245, 183, 74, 0.08);
    }
    svg {
      flex: none;
    }
    .track {
      fill: none;
      stroke: rgba(245, 183, 74, 0.2);
      stroke-width: 3;
    }
    .ring {
      fill: none;
      stroke: var(--warn);
      stroke-width: 3;
      stroke-linecap: round;
      transform: rotate(-90deg);
      transform-origin: 50% 50%;
      transition: stroke-dashoffset 1s linear;
    }
    text {
      fill: var(--warn-text);
      font-size: 11px;
      font-weight: 700;
    }
    .text {
      display: flex;
      flex-direction: column;
      font-size: 13px;
      color: var(--text-2);
    }
    strong {
      font-size: 14px;
      color: var(--warn-text);
    }
  `,
  host: { role: 'status' },
})
export class RetryCountdown {
  readonly seconds = input.required<number>();
  readonly done = output<void>();

  private readonly elapsed = signal(0);
  protected readonly ring = RING;
  protected readonly left = computed(() => Math.max(0, this.seconds() - this.elapsed()));
  protected readonly offset = computed(() => RING * (1 - this.left() / Math.max(this.seconds(), 1)));

  constructor() {
    const timer = setInterval(() => {
      this.elapsed.update((s) => s + 1);
      if (this.left() === 0) {
        clearInterval(timer);
        this.done.emit();
      }
    }, 1_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
