import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, switchMap } from 'rxjs';
import { SessionData } from './session';

interface TokenResponse {
  readonly accessToken: string;
  readonly expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  login(email: string, password: string): Observable<SessionData> {
    return this.http
      .post<TokenResponse>('/api/auth/login', { email, password })
      .pipe(map((t) => ({ token: t.accessToken, expiresAt: t.expiresAt, email })));
  }

  /** Zaklada konto i od razu loguje — nowy klient nie musi wpisywac danych drugi raz. */
  register(email: string, password: string): Observable<SessionData> {
    return this.http.post('/api/auth/register', { email, password }).pipe(switchMap(() => this.login(email, password)));
  }
}
