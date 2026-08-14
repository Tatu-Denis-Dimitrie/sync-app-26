import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ImpersonationService } from '../services/impersonation.service';

const IMPERSONATION_READ_ONLY_CODE = 'IMPERSONATION_READ_ONLY';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const impersonation = inject(ImpersonationService);

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
      }

      // Always rethrow: component-level error handlers (toasts, form errors, etc.) still need to run.
      return throwError(() => err);
    })
  );
};
