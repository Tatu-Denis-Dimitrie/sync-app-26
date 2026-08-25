import { HttpInterceptorFn } from '@angular/common/http';

// The session lives in an httpOnly cookie now, invisible to (and unreadable by) this code - the
// browser attaches it on its own. withCredentials just tells XHR/fetch to send/accept cookies at
// all, same-origin or not.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req.clone({ withCredentials: true }));
};
