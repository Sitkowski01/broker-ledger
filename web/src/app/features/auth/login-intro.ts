import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Logo } from '../../shared/logo';

/** Lewa strona ekranu logowania: o co chodzi w aplikacji i przykladowy dokument Wn/Ma. */
@Component({
  selector: 'bl-login-intro',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Logo],
  templateUrl: './login-intro.html',
  styleUrl: './login-intro.css',
})
export class LoginIntro {}
