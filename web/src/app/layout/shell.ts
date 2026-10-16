import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../core/auth/session';
import { ClientEvents } from '../core/events/client-events';
import { QuoteFeed } from '../core/market/quote-feed';
import { OrderNotifications } from '../core/orders/order-notifications';
import { DEMO } from '../demo/demo-mode';
import { Logo } from '../shared/logo';
import { ConnectionBanner } from './connection-banner';
import { Ticker } from './ticker';
import { ToastStack } from './toast-stack';

/** Rama zalogowanej czesci: notowania, naglowek z nawigacja, tresc ekranu i stopka. */
@Component({
  selector: 'bl-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo, Ticker, ToastStack, ConnectionBanner],
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

  protected readonly demo = DEMO;

  protected readonly links = [
    { path: '/', label: 'Pulpit', exact: true },
    { path: '/zlecenia', label: 'Zlecenia', exact: false },
    { path: '/ksiega', label: 'Księga', exact: false },
  ];

  constructor() {
    // Jedno polaczenie ze strumieniem zdarzen na cala zalogowana czesc — ekrany sie pod nie podpinaja.
    this.events.events$.pipe(takeUntilDestroyed()).subscribe();
    inject(OrderNotifications).start(inject(DestroyRef));
  }

  protected logout(): void {
    this.session.end();
    void this.router.navigateByUrl('/zaloguj');
  }
}
