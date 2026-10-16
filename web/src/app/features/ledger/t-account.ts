import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TAccount } from './t-account-model';

/** Konto T dostepnych zlotowek za dzis: Wn po lewej, Ma po prawej, obie strony sumuja sie do tej samej kwoty. */
@Component({
  selector: 'bl-t-account',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar">
      <h2 id="t-title">Konto T</h2>
      <span class="meta">Dostępne PLN · dziś</span>
    </div>
    <div class="body num">
      <div class="t">
        <div class="side wn">
          <h3 class="micro">Wn</h3>
          @for (l of account().debit; track $index) {
            <div class="line" [class]="l.role"><span class="what">{{ l.label }}</span><span>{{ l.amount }}</span></div>
          }
        </div>
        <div class="side ma">
          <h3 class="micro">Ma</h3>
          @for (l of account().credit; track $index) {
            <div class="line" [class]="l.role"><span class="what">{{ l.label }}</span><span>{{ l.amount }}</span></div>
          }
        </div>
      </div>
      <div class="totals">
        <div class="line"><span class="micro">Razem</span><span>{{ account().total }}</span></div>
        <div class="line"><span class="micro">Razem</span><span>{{ account().total }}</span></div>
      </div>
      <p class="note">
        @if (account().empty) {
          Dziś nie było ruchu na dostępnych złotówkach — saldo
        } @else {
          Saldo końcowe
        }
        <span class="num close">{{ account().closing }} zł</span> domyka obie strony — to ta sama kwota, którą widzisz na pulpicie.
      </p>
    </div>
  `,
  styleUrl: './t-account.css',
  host: { class: 'panel neon', role: 'region', 'aria-labelledby': 't-title' },
})
export class TAccountPanel {
  readonly account = input.required<TAccount>();
}
