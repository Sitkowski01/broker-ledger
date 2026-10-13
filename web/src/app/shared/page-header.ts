import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Naglowek ekranu: maly napis nad tytulem i linia, ktora wygasa w prawo. */
@Component({
  selector: 'bl-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="kicker">{{ kicker() }}</p>
    <div class="row">
      <h1 class="display">{{ title() }}</h1>
      <span class="line" aria-hidden="true"></span>
    </div>
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 20px;
    }
    h1 {
      font-size: clamp(32px, 6vw, 40px);
    }
    .line {
      flex: 1;
      height: 1px;
      background: linear-gradient(to right, var(--bull-line), var(--line), transparent);
    }
  `,
})
export class PageHeader {
  readonly kicker = input.required<string>();
  readonly title = input.required<string>();
}
