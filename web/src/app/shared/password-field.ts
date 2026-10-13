import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

/** Pole hasla z przelacznikiem "pokaz / ukryj" — zeby sprawdzic, co sie wpisalo, bez zgadywania. */
@Component({
  selector: 'bl-password-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  host: { class: 'field', '[class.invalid]': 'invalid()' },
  template: `
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
      <rect x="4" y="10" width="16" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </svg>
    <input [id]="inputId()" [type]="visible() ? 'text' : 'password'" [formControl]="control()"
           [attr.autocomplete]="autocomplete()" [attr.aria-invalid]="invalid()" [attr.aria-describedby]="describedBy()" />
    <button type="button" class="icon-btn" (click)="visible.set(!visible())"
            [attr.aria-pressed]="visible()" [attr.aria-label]="visible() ? 'Ukryj hasło' : 'Pokaż hasło'">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
        <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" />
      </svg>
    </button>
  `,
})
export class PasswordField {
  readonly control = input.required<FormControl<string>>();
  readonly inputId = input.required<string>();
  readonly autocomplete = input<'current-password' | 'new-password'>('current-password');
  readonly invalid = input(false);
  readonly describedBy = input<string | null>(null);
  protected readonly visible = signal(false);
}
