import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { TranslationService } from '../services/translation.service';

export const languageInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  const language = inject(TranslationService).language();
  return next(req.clone({ setHeaders: { 'X-App-Language': language } }));
};
