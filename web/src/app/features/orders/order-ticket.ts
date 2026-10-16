import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { formatCents, formatQuantity, parseCents } from '../../core/format/money';
import { companyName, SYMBOLS } from '../../core/market/companies';
import { OrderSide, OrderType } from '../../core/orders/order-api';
import { ctaText, parseQuantity, summaryRows, ticketErrors, TicketInput } from './ticket-model';

/** Formularz zlecenia. Pokazuje, ile ksiega zablokuje, zanim klient kliknie. */
@Component({
  selector: 'bl-order-ticket',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './order-ticket.html',
  host: {
    class: 'panel',
    role: 'region',
    'aria-labelledby': 'ticket-title',
    '[class.neon]': "side() === 'buy'",
    '[class.neon-sell]': "side() === 'sell'",
  },
})
export class OrderTicket {
  private readonly form = inject(NonNullableFormBuilder);

  /** Wolne zlotowki w groszach. */
  readonly available = input.required<number>();
  /** Wolne akcje kazdej spolki (symbol -> sztuki). */
  readonly holdings = input<Readonly<Record<string, number>>>({});
  /** Ostatnie ceny z notowan (symbol -> "248.40"). */
  readonly prices = input<Readonly<Record<string, string>>>({});

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

  protected readonly submitted = signal(false);

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
  protected readonly priceError = computed(() => (this.submitted() ? this.errors().price : null));
  protected readonly quantityError = computed(() => {
    const { quantity, short } = this.errors();
    return this.submitted() || short ? quantity : null;
  });

  protected readonly summary = computed(() => summaryRows(this.ticket(), this.lastCents()));
  protected readonly cta = computed(() => ctaText(this.side(), this.ticket().quantity, this.symbolValue()));

  protected readonly heldText = computed(() => {
    const held = formatQuantity(this.held());
    return this.side() === 'buy' ? `Na rachunku: ${held} szt.` : `Do sprzedaży: ${held} szt.`;
  });

  protected setSide(side: OrderSide): void {
    this.side.set(side);
    this.submitted.set(false);
  }

  protected setType(type: OrderType): void {
    this.chosenType.set(type);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.submitted.set(true);
  }
}
