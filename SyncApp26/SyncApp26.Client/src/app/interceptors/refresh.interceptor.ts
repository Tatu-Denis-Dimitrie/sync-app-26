import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, finalize, shareReplay, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';

// Never attempt a refresh for these: the auth endpoints establish or end a session (no access
// token to refresh yet, or deliberately none anymore), and /refresh and /me are the refresh
// mechanism's own plumbing - refreshing on their 401 would recurse or be meaningless.
const REFRESH_EXEMPT_PATHS = [
  '/authentication/login',
  '/authentication/register',
  '/authentication/google-login',
  '/authentication/microsoft-login',
  '/authentication/forgot-password',
  '/authentication/reset-password',
  '/authentication/refresh',
  '/authentication/me'
];

// Module-level, not per-call: a functional interceptor has no instance to hold this on, and it
// must be shared across every concurrent request hitting a 401 at once, or two tabs/requests
// racing on an expired token would each fire their own POST /refresh.
let refreshInFlight: Observable<unknown> | null = null;

function refreshSession(http: HttpClient): Observable<unknown> {
  if (!refreshInFlight) {
    refreshInFlight = http.post(`${environment.apiUrl}/authentication/refresh`, {}).pipe(
      finalize(() => { refreshInFlight = null; }),
      shareReplay(1)
    );
  }
  return refreshInFlight;
}

export const refreshInterceptor: HttpInterceptorFn = (req, next) => {
  if (REFRESH_EXEMPT_PATHS.some(path => req.url.includes(path))) {
    return next(req);
  }

  const http = inject(HttpClient);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401) {
        return throwError(() => err);
      }

      return refreshSession(http).pipe(
        switchMap(() => next(req)),
        // The refresh itself failed (no valid refresh token, e.g. an impersonation session, or it
        // was revoked) - surface the ORIGINAL 401, not the refresh call's own error, so
        // errorInterceptor's usual handling (impersonation-stop / logout) reacts to it normally.
        catchError(() => throwError(() => err))
      );
    })
  );
};
