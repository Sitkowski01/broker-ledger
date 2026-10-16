import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Panel w trakcie wczytywania: pasek tytulowy i szare ksztalty tam, gdzie pojawia sie tresc. Uklad nie skacze,
 * gdy przyjda dane, a czytnik ekranu slyszy "Wczytywanie" z tytulem panelu (aria-busy).
 */
@Component({
  selector: 'bl-skeleton-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar">
      <h2>{{ title() }}</h2>
      <span class="meta">Wczytywanie…</span>
    </div>
    <div class="body" aria-hidden="true">
      @if (headline()) {
        <div class="sk big"></div>
        <div class="sk mid"></div>
      }
      @for (row of rowList(); track $index) {
        <div class="row">
          <div class="sk badge"></div>
          <div class="lines"><div class="sk line" [style.width]="row"></div><div class="sk small"></div></div>
          <div class="sk value"></div>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .body {
      display: flex;
      flex-direction: column;
      gap: 18px;
      padding: 24px 20px;
    }
    .sk {
      border-radius: 6px;
      background: linear-gradient(90deg, var(--line) 0%, #273449 40%, var(--line) 80%);
      background-size: 300% 100%;
      animation: shimmer 1.4s ease-in-out infinite;
    }
    .big {
      width: 62%;
      height: 52px;
    }
    .mid {
      width: 38%;
      height: 22px;
    }
    .row {
      display: grid;
      grid-template-columns: 44px 1fr 90px;
      gap: 14px;
      align-items: center;
    }
    .badge {
      width: 44px;
      height: 44px;
      border-radius: 10px;
    }
    .lines {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .line {
      height: 14px;
    }
    .small {
      width: 30%;
      height: 10px;
    }
    .value {
      height: 14px;
    }
    @keyframes shimmer {
      from {
        background-position: 100% 0;
      }
      to {
        background-position: -50% 0;
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .sk {
        animation: none;
      }
    }
  `,
  host: { class: 'panel', 'aria-busy': 'true', '[attr.aria-label]': "'Wczytywanie: ' + title()" },
})
export class SkeletonPanel {
  readonly title = input.required<string>();
  /** Duza liczba na gorze (np. wartosc rachunku). */
  readonly headline = input(false);
  readonly rows = input(3);

  /** Rozne szerokosci wierszy, zeby szkielet nie wygladal jak tabela z jednym tekstem. */
  protected readonly rowList = computed(() => Array.from({ length: this.rows() }, (_, i) => ['46%', '38%', '52%', '42%'][i % 4]));
}
