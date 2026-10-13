import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../core/auth/session';
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

  private readonly session = inject(Session);
  private readonly router = inject(Router);
  protected readonly email = computed(() => this.session.current()?.email ?? '');
  protected readonly initials = computed(() => this.email().slice(0, 2).toUpperCase());

  protected readonly links = [
    { path: '/', label: 'Pulpit', exact: true },
    { path: '/zlecenia', label: 'Zlecenia', exact: false },
    { path: '/ksiega', label: 'Księga', exact: false },
  ];

  protected logout(): void {
    this.session.end();
    void this.router.navigateByUrl('/zaloguj');
  }
}
