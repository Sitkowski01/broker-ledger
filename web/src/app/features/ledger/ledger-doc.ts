import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { RouterLink } from '@angular/router';
import { shortId } from '../../core/format/ids';
import { DocView } from './ledger-model';

/** Jeden dokument ksiegi: naglowek (rodzaj, opis, kwota, godzina) i po rozwinieciu zapisy Wn/Ma z bilansem. */
@Component({
  selector: 'bl-ledger-doc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './ledger-doc.html',
  host: { class: 'panel' },
})
export class LedgerDoc {
  private readonly document = inject(DOCUMENT);

  readonly doc = input.required<DocView>();
  readonly time = input.required<string>();
  readonly open = input(false);
  readonly toggle = output<void>();

  protected readonly copied = signal(false);
  protected readonly shortId = shortId;

  /** Kopiuje klucz dokumentu — po nim szuka sie zdarzenia w logach albo w bazie. */
  protected async copyKey(key: string): Promise<void> {
    try {
      await this.document.defaultView?.navigator.clipboard.writeText(key);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2_000);
    } catch {
      // Schowek bywa niedostepny (np. strona po HTTP z innego komputera) — klucz i tak widac obok.
    }
  }
}
