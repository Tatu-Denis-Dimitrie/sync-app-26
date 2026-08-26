import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { switchMap } from 'rxjs';

import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { refreshInterceptor } from './interceptors/refresh.interceptor';
import { AuthenticationService } from './services/authentication.service';
import { TranslationService } from './services/translation.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // Response/error unwinds right-to-left, so refreshInterceptor (last) sees a 401 before
    // errorInterceptor's logout/impersonation-stop fallback does.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, refreshInterceptor])),
    provideAnimations(),
    provideAppInitializer(() => {
      const authService = inject(AuthenticationService);
      const translationService = inject(TranslationService);
      return authService.hydrate().pipe(switchMap(() => translationService.initialize()));
    })
  ]
};
