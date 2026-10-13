import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiError, toApiError } from '../../core/api/api-error';
import { AuthApi } from '../../core/auth/auth-api';
import { Session } from '../../core/auth/session';
import { safeReturnUrl } from '../../core/navigation/return-url';
import { PasswordField } from '../../shared/password-field';
import { LoginIntro } from './login-intro';

type Mode = 'login' | 'register';

@Component({
  selector: 'bl-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, LoginIntro, PasswordField],
  templateUrl: './login-page.html',
  styleUrl: './login-page.css',
})
export class LoginPage {
  private readonly auth = inject(AuthApi);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly mode = signal<Mode>('login');
  protected readonly pending = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly submitted = signal(false);
  /** Ekran mowi, ze sesja wygasla, tylko gdy klient tu trafil przez wygasniecie. */
  protected readonly expired = this.session.expired();

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128)]],
  });

  protected readonly generalError = computed(() => {
    const error = this.error();
    if (error?.kind === 'conflict') {
      return 'Ten e-mail ma już konto. Zaloguj się.';
    }
    return error && error.kind !== 'validation' ? error.message : null;
  });

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
    this.error.set(null);
  }

  /** Komunikat pod polem: najpierw blad z serwera, potem walidacja w przegladarce (po probie wyslania). */
  protected fieldError(field: 'email' | 'password'): string | null {
    const error = this.error();
    if (error?.kind === 'validation' && error.fields[field]) {
      return error.fields[field];
    }
    const control = this.form.controls[field];
    if (!this.submitted() || control.valid) {
      return null;
    }
    if (field === 'email') {
      return 'Podaj poprawny adres e-mail.';
    }
    return this.mode() === 'register' || control.hasError('required')
      ? 'Hasło musi mieć od 8 do 128 znaków.'
      : null;
  }

  protected submit(): void {
    this.submitted.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    // Przy logowaniu nie zdradzamy zasad hasla — wystarczy, ze pole nie jest puste.
    const ready = this.mode() === 'register' ? this.form.valid : this.form.controls.email.valid && password !== '';
    if (!ready || this.pending()) {
      return;
    }
    this.pending.set(true);
    const call = this.mode() === 'register' ? this.auth.register(email, password) : this.auth.login(email, password);
    call.subscribe({
      next: (data) => {
        this.session.start(data);
        void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('powrot')));
      },
      error: (e: unknown) => {
        this.pending.set(false);
        this.error.set(toApiError(e));
      },
    });
  }
}
