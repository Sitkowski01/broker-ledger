import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../core/auth/session';
import { ClientEvents } from '../core/events/client-events';
import { QuoteFeed } from '../core/market/quote-feed';
import { Logo } from '../shared/logo';
import { Ticker } from './ticker';

/** Rama zalogowanej czesci: notowania, naglowek z nawigacja, tresc ekranu i stopka. */
@Component({
  selector: 'bl-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo, Ticker],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  protected readonly quotes = inject(QuoteFeed).items;
  private readonly events = inject(ClientEvents);
  protected readonly streamStatus = this.events.status;

  private readonly session = inject(Session);
  private readonly router = inject(Router);
  protected readonly email = computed(() => this.session.current()?.email ?? '');
  protected readonly initials = computed(() => this.email().slice(0, 2).toUpperCase());

  protected readonly links = [
    { path: '/', label: 'Pulpit', exact: true },
    { path: '/zlecenia', label: 'Zlecenia', exact: false },
    { path: '/ksiega', label: 'Księga', exact: false },
  ];

  constructor() {
    // Jedno polaczenie ze strumieniem zdarzen na cala zalogowana czesc — ekrany sie pod nie podpinaja.
    this.events.events$.pipe(takeUntilDestroyed()).subscribe();
  }

  protected logout(): void {
    this.session.end();
    void this.router.navigateByUrl('/zaloguj');
  }
}
