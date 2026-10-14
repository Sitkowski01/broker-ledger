import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, TransferKind } from '../../core/account/account-api';
import { ApiError, toApiError } from '../../core/api/api-error';
import { newIdempotencyKey } from '../../core/api/idempotency-key';
import { formatCents, parseCents } from '../../core/format/money';
import { shareOf, transferError, TransferRow } from './transfer-model';

const DEPOSIT_STEPS = [50_000, 100_000, 500_000];
const WITHDRAWAL_SHARES = [25, 50, 100];

/**
 * Wplata i wyplata. Kazda proba przelewu dostaje klucz idempotencji; ponowienie tej samej kwoty
 * (np. po zerwanym polaczeniu) wysyla ten sam klucz, wiec ksiega nie zaksieguje przelewu drugi raz.
 */
@Component({
  selector: 'bl-transfer-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './transfer-panel.html',
  styleUrl: './transfer-panel.css',
  host: { class: 'panel', role: 'region', 'aria-labelledby': 'transfer-title' },
})
export class TransferPanel {
  private readonly api = inject(AccountApi);

  /** Wolne zlotowki w groszach — tyle mozna wyplacic. */
  readonly available = input.required<number>();
  readonly recent = input<readonly TransferRow[]>([]);
  /** Przelew trafil do ksiegi — rodzic odswieza saldo. */
  readonly posted = output<void>();

  protected readonly kind = signal<TransferKind>('deposit');
  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly done = signal<string | null>(null);

  protected readonly amount = inject(NonNullableFormBuilder).control(formatCents(100_000));
  private readonly amountText = toSignal(this.amount.valueChanges, { initialValue: this.amount.value });
  protected readonly cents = computed(() => parseCents(this.amountText()));

  /** Klucz biezacej proby: nowy po udanym przelewie i po zmianie kwoty lub rodzaju. */
  private key = newIdempotencyKey();

  protected readonly quick = computed(() =>
    this.kind() === 'deposit'
      ? DEPOSIT_STEPS.map((step) => ({ label: `+${formatCents(step).replace(',00', '')}`, step, share: 0 }))
      : WITHDRAWAL_SHARES.map((share) => ({ label: share === 100 ? 'Całość' : `${share}%`, step: 0, share })),
  );

  protected readonly availableText = computed(() => formatCents(this.available()));

  protected readonly localError = computed(() => transferError(this.kind(), this.cents(), this.available()));

  protected readonly fieldError = computed(() => {
    const error = this.error();
    if (error?.kind === 'validation') {
      return error.fields['amount'] ?? error.fields['currency'] ?? 'Niepoprawna wartość.';
    }
    return this.submitted() ? this.localError() : null;
  });

  protected readonly generalError = computed(() => {
    const error = this.error();
    return error && error.kind !== 'validation' ? error.message : null;
  });

  protected readonly cta = computed(() => {
    const cents = this.cents();
    const verb = this.kind() === 'deposit' ? 'Wpłać' : 'Wypłać';
    return cents && cents > 0 && !this.localError() ? `${verb} ${formatCents(cents)} zł` : verb;
  });

  constructor() {
    this.amount.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.newAttempt());
  }

  protected setKind(kind: TransferKind): void {
    this.kind.set(kind);
    this.submitted.set(false);
    this.newAttempt();
  }

  protected applyQuick(q: { step: number; share: number }): void {
    const cents = q.share ? shareOf(this.available(), q.share) : Math.max(this.cents() ?? 0, 0) + q.step;
    this.amount.setValue(formatCents(cents));
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.submitted.set(true);
    this.error.set(null);
    this.done.set(null);
    const cents = this.cents();
    if (this.localError() || cents === null || this.pending()) {
      return;
    }
    this.pending.set(true);
    const kind = this.kind();
    const key = this.key;
    // Klient moze zmienic kwote albo rodzaj, zanim przyjdzie odpowiedz — wtedy formularz to juz inna proba.
    const current = () => key === this.key;
    this.api.transfer(kind, cents, key).subscribe({
      next: () => {
        this.pending.set(false);
        if (current()) {
          this.submitted.set(false);
          this.key = newIdempotencyKey();
        }
        // Przelew jest w ksiedze niezaleznie od tego, co jest teraz w formularzu — klient musi to zobaczyc.
        this.done.set(`${kind === 'deposit' ? 'Wpłata' : 'Wypłata'} ${formatCents(cents)} zł zaksięgowana.`);
        this.posted.emit();
      },
      // Klucz zostaje: ponowienie tej samej kwoty to ta sama operacja. Blad starej proby nie dotyczy nowej.
      error: (e: unknown) => {
        this.pending.set(false);
        if (current()) {
          this.error.set(toApiError(e));
        }
      },
    });
  }

  private newAttempt(): void {
    this.key = newIdempotencyKey();
    this.error.set(null);
    this.done.set(null);
  }
}
