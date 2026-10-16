import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Jak czytac dokumenty — cztery zasady ksiegi podwojnego zapisu. */
@Component({
  selector: 'bl-ledger-guide',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar"><h2 id="how-title">Jak czytać</h2></div>
    <ol>
      <li><span class="num">01</span><span>Suma Wn równa się sumie Ma osobno dla każdego aktywa — złotówek i akcji każdej spółki.</span></li>
      <li>
        <span class="num">02</span>
        <span><strong class="mine">Twoje</strong> konta to Twoje środki; konta <strong>brokera</strong> — bank, depozyt, prowizje — pokazują drugą stronę operacji.</span>
      </li>
      <li><span class="num">03</span><span>Dokument ma klucz, więc powtórzone zdarzenie z giełdy nie zaksięguje się drugi raz.</span></li>
      <li><span class="num">04</span><span>Dokumentów się nie poprawia ani nie usuwa. Pomyłkę koryguje nowy dokument.</span></li>
    </ol>
  `,
  styles: `
    :host {
      display: block;
    }
    ol {
      margin: 0;
      padding: 8px 20px 12px;
      list-style: none;
      font-size: 14px;
      color: var(--text-2);
    }
    li {
      display: grid;
      grid-template-columns: 28px 1fr;
      gap: 8px;
      padding: 10px 0;
    }
    li + li {
      border-top: 1px solid rgba(30, 41, 59, 0.5);
    }
    .num {
      color: var(--bull);
      font-size: 12px;
      font-weight: 700;
    }
    strong {
      color: var(--text);
      font-weight: 600;
    }
    .mine {
      color: var(--bull-light);
    }
  `,
  host: { class: 'panel', role: 'region', 'aria-labelledby': 'how-title' },
})
export class LedgerGuide {}
