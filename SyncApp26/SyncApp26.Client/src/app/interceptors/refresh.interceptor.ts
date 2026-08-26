import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, finalize, shareReplay, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';

// These establish/end a session or are the refresh mechanism's own plumbing - refreshing on their
// 401 would recurse or be meaningless.
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

// Module-level so concurrent 401s share one in-flight refresh instead of each firing their own.
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
        // Refresh itself failed - surface the ORIGINAL 401 so errorInterceptor reacts to it normally.
        catchError(() => throwError(() => err))
      );
    })
  );
};
