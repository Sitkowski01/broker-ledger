import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Znak marki: konto "T" z ksiegowosci — lewa strona Wn, prawa Ma. */
@Component({
  selector: 'bl-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg width="30" height="30" viewBox="0 0 30 30" aria-hidden="true">
      <rect x="1" y="1" width="28" height="28" rx="7" fill="rgba(16,185,129,.08)" stroke="#10b981" stroke-width="1.5" />
      <path d="M8 10h14M15 10v12" stroke="#10b981" stroke-width="2" stroke-linecap="round" />
      <circle cx="10.5" cy="15" r="1.2" fill="#10b981" />
      <circle cx="19.5" cy="15" r="1.2" fill="#10b981" />
    </svg>
    <span>Broker<span class="dot">·</span>Ledger</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      font-family: var(--display);
      font-size: 18px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: -0.01em;
      color: var(--text);
    }
    .dot {
      color: var(--bull);
    }
  `,
})
export class Logo {}
