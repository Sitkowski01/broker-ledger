import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { merge } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { newIdempotencyKey } from '../../core/api/idempotency-key';
import { formatCents, formatQuantity, parseCents } from '../../core/format/money';
import { companyName, SYMBOLS } from '../../core/market/companies';
import { OrderApi, OrderDto, OrderSide, OrderType } from '../../core/orders/order-api';
import { RetryCountdown } from '../../shared/retry-countdown';
import { placedMessage, rejectionText } from './order-model';
import { ctaText, maxBuyQuantity, parseQuantity, summaryRows, ticketErrors, TicketInput } from './ticket-model';

/**
 * Formularz zlecenia. Pokazuje, ile ksiega zablokuje, zanim klient kliknie; kazda proba ma klucz idempotencji,
 * wiec podwojne klikniecie albo ponowienie po zerwanym polaczeniu nie zlozy drugiego zlecenia.
 */
@Component({
  selector: 'bl-order-ticket',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RetryCountdown],
  templateUrl: './order-ticket.html',
  styleUrl: './order-ticket.css',
  host: {
    class: 'panel',
    role: 'region',
    'aria-labelledby': 'ticket-title',
    '[class.neon]': "side() === 'buy'",
    '[class.neon-sell]': "side() === 'sell'",
  },
})
export class OrderTicket {
  private readonly api = inject(OrderApi);
  private readonly form = inject(NonNullableFormBuilder);

  /** Wolne zlotowki w groszach. */
  readonly available = input.required<number>();
  /** Wolne akcje kazdej spolki (symbol -> sztuki). */
  readonly holdings = input<Readonly<Record<string, number>>>({});
  /** Ostatnie ceny z notowan (symbol -> "248.40"). */
  readonly prices = input<Readonly<Record<string, string>>>({});
  /** Zlecenie zlozone (takze odrzucone przez gielde) — rodzic odswieza liste i salda. */
  readonly placed = output<OrderDto>();

  protected readonly symbols = SYMBOLS.map((symbol) => ({ symbol, name: companyName(symbol) }));
  protected readonly side = signal<OrderSide>('buy');
  /** Kupno zawsze z limitem: bez ceny nie wiadomo, ile zablokowac (Order.Place). */
  private readonly chosenType = signal<OrderType>('limit');
  protected readonly type = computed<OrderType>(() => (this.side() === 'buy' ? 'limit' : this.chosenType()));

  protected readonly symbol = this.form.control(SYMBOLS[0]);
  protected readonly price = this.form.control('');
  protected readonly quantity = this.form.control('10');
  protected readonly symbolValue = toSignal(this.symbol.valueChanges, { initialValue: this.symbol.value });
  private readonly priceText = toSignal(this.price.valueChanges, { initialValue: this.price.value });
  private readonly quantityText = toSignal(this.quantity.valueChanges, { initialValue: this.quantity.value });

  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly rejected = signal<string | null>(null);
  protected readonly done = signal<string | null>(null);

  /** Klucz biezacej proby: nowy po zlozonym zleceniu i po kazdej zmianie formularza. */
  private key = newIdempotencyKey();

  protected readonly lastCents = computed(() => {
    const last = this.prices()[this.symbolValue()];
    return last ? parseCents(last) : null;
  });
  protected readonly lastText = computed(() => {
    const last = this.lastCents();
    return last === null ? null : formatCents(last);
  });
  protected readonly held = computed(() => this.holdings()[this.symbolValue()] ?? 0);

  private readonly ticket = computed<TicketInput>(() => ({
    side: this.side(),
    type: this.type(),
    limitCents: parseCents(this.priceText()),
    quantity: parseQuantity(this.quantityText()),
    availableCents: this.available(),
    heldShares: this.held(),
  }));

  private readonly errors = computed(() => ticketErrors(this.ticket()));
  protected readonly priceError = computed(() => this.fieldError('limitPrice') ?? (this.submitted() ? this.errors().price : null));
  protected readonly quantityError = computed(() => {
    const { quantity, short } = this.errors();
    return this.fieldError('quantity') ?? (this.submitted() || short ? quantity : null);
  });
  /** 429: ile sekund odczekac — zamiast komunikatu bledu pokazujemy odliczanie. */
  protected readonly retryAfter = computed(() => {
    const error = this.error();
    return error?.kind === 'rate-limited' ? error.retryAfterSeconds : null;
  });

  protected readonly generalError = computed(() => {
    const error = this.error();
    if (!error) {
      return null;
    }
    if (error.kind === 'validation') {
      return error.fields['order'] ?? error.fields['symbol'] ?? null;
    }
    return error.message;
  });

  protected readonly maxQuantity = computed(() => {
    if (this.side() === 'sell') {
      return this.held();
    }
    const limit = parseCents(this.priceText());
    return limit === null ? 0 : maxBuyQuantity(this.available(), limit);
  });

  protected readonly summary = computed(() => summaryRows(this.ticket(), this.lastCents()));
  protected readonly cta = computed(() => ctaText(this.side(), this.ticket().quantity, this.symbolValue()));

  protected readonly heldText = computed(() => {
    const held = formatQuantity(this.held());
    return this.side() === 'buy' ? `Na rachunku: ${held} szt.` : `Do sprzedaży: ${held} szt.`;
  });

  constructor() {
    merge(this.symbol.valueChanges, this.price.valueChanges, this.quantity.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.newAttempt());
  }

  protected setSide(side: OrderSide): void {
    this.side.set(side);
    this.submitted.set(false);
    this.newAttempt();
  }

  protected setType(type: OrderType): void {
    this.chosenType.set(type);
    this.newAttempt();
  }

  protected useLast(): void {
    const last = this.lastCents();
    if (last !== null) {
      this.price.setValue(formatCents(last));
    }
  }

  protected useMax(): void {
    this.quantity.setValue(String(this.maxQuantity()));
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.pending()) {
      return;
    }
    this.submitted.set(true);
    this.error.set(null);
    this.rejected.set(null);
    this.done.set(null);
    const t = this.ticket();
    const errors = this.errors();
    if (errors.price || errors.quantity || t.quantity === null) {
      return;
    }
    this.pending.set(true);
    const key = this.key;
    const current = () => key === this.key;
    const order = { symbol: this.symbolValue(), side: t.side, type: t.type, limitCents: t.type === 'limit' ? t.limitCents : null, quantity: t.quantity };
    this.api.place(order, key).subscribe({
      next: (placed) => {
        this.pending.set(false);
        if (current()) {
          this.submitted.set(false);
          this.key = newIdempotencyKey();
        }
        this.done.set(placedMessage(placed));
        this.placed.emit(placed);
      },
      error: (e: unknown) => {
        this.pending.set(false);
        // 422 ze zleceniem w tresci: ksiega albo gielda odmowila. Zlecenie istnieje (odrzucone), wiec trafia na liste.
        const body = e instanceof HttpErrorResponse && e.status === 422 ? (e.error as Partial<OrderDto> | null) : null;
        if (body?.id && body.status === 'rejected') {
          if (current()) {
            this.key = newIdempotencyKey();
            this.rejected.set(rejectionText(body.rejectionReason ?? null));
          }
          this.placed.emit(body as OrderDto);
          return;
        }
        // Klucz zostaje: ponowienie tego samego zlecenia to ta sama operacja.
        if (current()) {
          this.error.set(toApiError(e));
        }
      },
    });
  }

  private fieldError(field: string): string | null {
    const error = this.error();
    return error?.kind === 'validation' ? (error.fields[field] ?? null) : null;
  }

  private newAttempt(): void {
    this.key = newIdempotencyKey();
    this.error.set(null);
    this.rejected.set(null);
    this.done.set(null);
  }
}
