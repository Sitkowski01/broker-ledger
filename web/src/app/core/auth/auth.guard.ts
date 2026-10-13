import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Session } from './session';

/** Ekrany rachunku tylko po zalogowaniu; po logowaniu wracamy tam, gdzie klient chcial wejsc. */
export const authGuard: CanActivateFn = (_route, state) =>
  inject(Session).isActive() ||
  inject(Router).createUrlTree(['/zaloguj'], { queryParams: state.url === '/' ? {} : { powrot: state.url } });
