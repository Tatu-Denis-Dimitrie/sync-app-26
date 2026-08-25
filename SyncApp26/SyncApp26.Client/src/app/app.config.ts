import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';

import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { refreshInterceptor } from './interceptors/refresh.interceptor';
import { AuthenticationService } from './services/authentication.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // Angular interceptors nest like middleware: request order is left-to-right, but a
    // response/error unwinds RIGHT-to-left - the last interceptor is closest to the backend and
    // sees it first. refreshInterceptor must be last so it gets first crack at a 401 (silent
    // refresh + retry) before errorInterceptor's logout/impersonation-stop fallback ever sees it.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, refreshInterceptor])),
    provideAnimations(),
    // Runs before the router's first navigation, so every guard (all synchronous) can rely on the
    // session already being resolved by the time it runs. hydrate() itself never throws/errors -
    // an app initializer that rejects aborts bootstrap entirely (blank page).
    provideAppInitializer(() => inject(AuthenticationService).hydrate())
  ]
};
