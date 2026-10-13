import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Logo } from '../shared/logo';
import { Ticker, TickerItem } from './ticker';

/** Rama zalogowanej czesci: notowania, naglowek z nawigacja, tresc ekranu i stopka. */
@Component({
  selector: 'bl-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo, Ticker],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  // Notowania podlaczy serwis rynku (krok 8c); do tego czasu pasek jest ukryty.
  protected readonly quotes = signal<readonly TickerItem[]>([]);

  protected readonly links = [
    { path: '/', label: 'Pulpit', exact: true },
    { path: '/zlecenia', label: 'Zlecenia', exact: false },
    { path: '/ksiega', label: 'Księga', exact: false },
  ];
}
