import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Session } from './session';

/**
 * Dokleja token do zapytan /api/me i /api/market. Gdy serwer odpowie 401 na zapytanie z tokenem,
 * sesja wygasa, a uzytkownik wraca do logowania z adresem, pod ktory chcial wejsc.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(Session);
  const router = inject(Router);
  const token = session.current()?.token;
  if (!token || !req.url.startsWith('/api/') || req.url.startsWith('/api/auth/')) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        session.expire();
        void router.navigate(['/zaloguj'], { queryParams: { powrot: router.url } });
      }
      return throwError(() => error);
    }),
  );
};
