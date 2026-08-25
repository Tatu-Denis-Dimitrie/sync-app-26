import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ImpersonationService } from '../services/impersonation.service';
import { AuthenticationService } from '../services/authentication.service';

const IMPERSONATION_READ_ONLY_CODE = 'IMPERSONATION_READ_ONLY';

// 401 from these is a normal business response or session-plumbing, not a rejected session.
const SESSION_EXEMPT_PATHS = [
  '/authentication/login',
  '/authentication/google-login',
  '/authentication/microsoft-login',
  '/authentication/me',
  '/authentication/logout',
  '/authentication/refresh'
];

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const impersonation = inject(ImpersonationService);
  const authentication = inject(AuthenticationService);
  const isSessionExempt = SESSION_EXEMPT_PATHS.some(path => req.url.includes(path));

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 403 && err.error?.code === IMPERSONATION_READ_ONLY_CODE) {
        impersonation.reportBlockedAction(
          err.error.message || 'This action is disabled while viewing as another user.');
      } else if (!isSessionExempt && err.status === 401 && impersonation.isImpersonating()) {
        // Impersonation has no refresh token, so an expired access token can't recover - drop back to the admin.
        impersonation.stop();
      } else if (!isSessionExempt && err.status === 401) {
        // Refresh already failed too - the session is dead, send the user back to log in.
        authentication.logout();
      }

      // Rethrow: component-level error handlers still need to run.
      return throwError(() => err);
    })
  );
};
