import { HttpInterceptorFn } from '@angular/common/http';

// Session lives in an httpOnly cookie the browser attaches on its own; withCredentials just
// tells XHR/fetch to send/accept cookies at all.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req.clone({ withCredentials: true }));
};
