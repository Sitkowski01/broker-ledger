import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Toasts } from '../core/notifications/toasts';

/** Powiadomienia w prawym dolnym rogu (na telefonie na dole, na cala szerokosc). */
@Component({
  selector: 'bl-toast-stack',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="stack" aria-live="polite">
      @for (t of toasts.items(); track t.id) {
        <div class="toast" [class]="'tone-' + t.tone" role="status">
          <span class="icon" aria-hidden="true">
            @switch (t.tone) {
              @case ('ok') {
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M5 12l5 5L20 7" /></svg>
              }
              @case ('bad') {
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>
              }
              @default {
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 6h16M12 6v14" /></svg>
              }
            }
          </span>
          <div class="text">
            <span class="title">{{ t.title }}</span>
            @for (line of t.lines; track $index) {
              <span class="num line">{{ line }}</span>
            }
            @if (t.link; as link) {
              <a [routerLink]="link.path" (click)="toasts.dismiss(t.id)">{{ link.label }}</a>
            }
          </div>
          <button type="button" class="icon-btn" (click)="toasts.dismiss(t.id)" aria-label="Zamknij powiadomienie">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg>
          </button>
          <span class="life" aria-hidden="true"></span>
        </div>
      }
    </div>
  `,
  styleUrl: './toast-stack.css',
})
export class ToastStack {
  protected readonly toasts = inject(Toasts);
}
