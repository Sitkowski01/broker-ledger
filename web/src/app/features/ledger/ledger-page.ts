import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { AccountApi, HistoryEntryDto, HistoryKind } from '../../core/account/account-api';
import { toApiError } from '../../core/api/api-error';
import { timeOf } from '../../core/format/dates';
import { PageHeader } from '../../shared/page-header';
import { byDay, docView, matchesSearch } from './ledger-model';
import { LedgerDoc } from './ledger-doc';
import { LedgerGuide } from './ledger-guide';

/** Dokumentow na strone listy. */
export const PAGE_SIZE = 20;

const FILTERS: readonly { key: HistoryKind | null; label: string }[] = [
  { key: null, label: 'Wszystkie' },
  { key: 'trades', label: 'Transakcje' },
  { key: 'blocks', label: 'Blokady' },
  { key: 'transfers', label: 'Przelewy' },
];

/** Ksiega: dokumenty Wn/Ma od najnowszych, filtr rodzaju (po stronie API) i wyszukiwanie. */
@Component({
  selector: 'bl-ledger-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, LedgerDoc, LedgerGuide, ReactiveFormsModule],
  templateUrl: './ledger-page.html',
})
export class LedgerPage {
  private readonly api = inject(AccountApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly filters = FILTERS;
  protected readonly kind = signal<HistoryKind | null>(null);
  protected readonly search = inject(NonNullableFormBuilder).control('');
  protected readonly query = toSignal(this.search.valueChanges, { initialValue: '' });

  private readonly entries = signal<HistoryEntryDto[]>([]);
  protected readonly next = signal<number | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private readonly opened = signal<ReadonlySet<number>>(new Set());
  private request: Subscription | null = null;

  protected readonly now = signal(new Date());
  private readonly docs = computed(() => this.entries().map(docView));
  protected readonly shown = computed(() => this.docs().filter((d) => matchesSearch(d, this.query())));
  protected readonly days = computed(() => byDay(this.shown(), this.now()));
  protected readonly allOpen = computed(() => this.shown().length > 0 && this.shown().every((d) => this.opened().has(d.sequence)));
  protected readonly timeOf = timeOf;

  constructor() {
    this.load(null);
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }

  protected isOpen(sequence: number): boolean {
    return this.opened().has(sequence);
  }

  protected setKind(kind: HistoryKind | null): void {
    this.kind.set(kind);
    this.load(null);
  }

  protected toggle(sequence: number): void {
    const opened = new Set(this.opened());
    if (!opened.delete(sequence)) {
      opened.add(sequence);
    }
    this.opened.set(opened);
  }

  protected toggleAll(): void {
    this.opened.set(this.allOpen() ? new Set() : new Set(this.shown().map((d) => d.sequence)));
  }

  protected loadMore(): void {
    this.load(this.next());
  }

  protected retry(): void {
    this.load(this.entries().length ? this.next() : null);
  }

  /** Pierwsza strona (before = null) zastepuje liste, kolejna dopisuje sie na koncu — kursor to numer dokumentu. */
  private load(before: number | null): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.now.set(new Date());
    this.request = this.api.history(PAGE_SIZE, before, this.kind()).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.entries.update((list) => (before === null ? [...page.items] : [...list, ...page.items]));
        this.next.set(page.nextBefore);
        if (before === null && page.items.length) {
          // Najnowszy dokument od razu rozwiniety — widac, jak wyglada dokument Wn/Ma.
          this.opened.set(new Set([page.items[0].sequence]));
        }
      },
      error: (e: unknown) => {
        this.loading.set(false);
        const api = toApiError(e);
        this.error.set('message' in api ? api.message : 'Nie udało się wczytać księgi.');
      },
    });
  }

}
