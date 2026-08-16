import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ImpersonationService } from '../services/impersonation.service';
import { AuthenticationService } from '../services/authentication.service';

const IMPERSONATION_READ_ONLY_CODE = 'IMPERSONATION_READ_ONLY';

// 401 from these is a normal "wrong credentials" business response from an anonymous endpoint, not
// a rejected session - they must never trigger the auto-logout below.
const CREDENTIAL_CHECK_PATHS = ['/authentication/login', '/authentication/google-login', '/authentication/microsoft-login'];

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const impersonation = inject(ImpersonationService);
  const authentication = inject(AuthenticationService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 403 && err.error?.code === IMPERSONATION_READ_ONLY_CODE) {
        impersonation.reportBlockedAction(
          err.error.message || 'This action is disabled while viewing as another user.');
      } else if (err.status === 401 && impersonation.isImpersonating()) {
        // The 30-minute impersonation token expired mid-session: drop back to the admin's own
        // session instead of stranding them on a dead token. NOT a blanket "401 -> logout" - that
        // would break the login page itself, which returns 401 for invalid credentials.
        impersonation.stop();
      } else if (err.status === 401 && !CREDENTIAL_CHECK_PATHS.some(path => req.url.includes(path))) {
        // The server just rejected a tampered, expired, or otherwise invalid session token still
        // sitting in localStorage - clear it and send the user back to log in properly rather than
        // stranding them on a UI that looks logged in but can't call anything.
        authentication.logout();
      }

      // Always rethrow: component-level error handlers (toasts, form errors, etc.) still need to run.
      return throwError(() => err);
    })
  );
};
