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
    // Response/error unwinds right-to-left, so refreshInterceptor (last) sees a 401 before
    // errorInterceptor's logout/impersonation-stop fallback does.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, refreshInterceptor])),
    provideAnimations(),
    // Runs before the router's first navigation, so guards can rely on the session being resolved.
    provideAppInitializer(() => inject(AuthenticationService).hydrate())
  ]
};
