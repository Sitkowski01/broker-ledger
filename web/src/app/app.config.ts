import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { ClientEvents } from './core/events/client-events';
import { DemoClientEvents, demoInterceptor } from './demo/demo-backend';
import { DEMO } from './demo/demo-mode';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // W trybie demo ostatni interceptor odpowiada zamiast serwera — reszta aplikacji o tym nie wie.
    provideHttpClient(withFetch(), withInterceptors(DEMO ? [authInterceptor, demoInterceptor] : [authInterceptor])),
    ...(DEMO ? [{ provide: ClientEvents, useExisting: DemoClientEvents }] : []),
  ],
};
