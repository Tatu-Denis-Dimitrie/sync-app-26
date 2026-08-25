import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ImpersonationService } from '../services/impersonation.service';
import { AuthenticationService } from '../services/authentication.service';

const IMPERSONATION_READ_ONLY_CODE = 'IMPERSONATION_READ_ONLY';

// 401 from these must never trigger the reactive branches below: login/google-login/microsoft-login
// return 401 as a normal "wrong credentials" business response, not a rejected session; me/logout/
// refresh are the session-management endpoints' own plumbing (refreshInterceptor already handles
// /refresh's failure by re-throwing the ORIGINAL request's 401, which reaches this interceptor
// separately - reacting here too would just be a redundant duplicate of that).
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
        // The impersonation access token expired (impersonation sessions have no refresh token) and
        // refreshInterceptor's attempt to recover already failed: drop back to the admin's own
        // session instead of stranding them on a dead token.
        impersonation.stop();
      } else if (!isSessionExempt && err.status === 401) {
        // Both the access token AND the refresh attempt are dead - the session truly cannot
        // continue. Send the user back to log in rather than stranding them on a UI that looks
        // logged in but can't call anything.
        authentication.logout();
      }

      // Always rethrow: component-level error handlers (toasts, form errors, etc.) still need to run.
      return throwError(() => err);
    })
  );
};
