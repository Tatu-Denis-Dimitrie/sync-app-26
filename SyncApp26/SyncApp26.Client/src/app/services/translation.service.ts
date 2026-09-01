import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthenticationService } from './authentication.service';

export const SUPPORTED_LANGUAGES = ['En'] as const;
export type Language = typeof SUPPORTED_LANGUAGES[number];

export const LANGUAGE_LABELS: Record<Language, string> = {
  En: 'English'
};

const DEFAULT_LANGUAGE: Language = 'En';

type TranslationCatalogue = Record<string, Record<string, string>>;

function isSupportedLanguage(value: string): value is Language {
  return (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

@Injectable({
  providedIn: 'root'
})
export class TranslationService {
  private http = inject(HttpClient);
  private authService = inject(AuthenticationService);
  private apiUrl = environment.apiUrl + environment.endpoints.localization;

  private readonly languageSignal = signal<Language>(DEFAULT_LANGUAGE);
  private readonly catalogueSignal = signal<TranslationCatalogue>({});

  readonly language = this.languageSignal.asReadonly();

  initialize(): Observable<void> {
    return this.loadLanguage(this.resolveInitialLanguage());
  }

  private resolveInitialLanguage(): Language {
    const stored = this.authService.getCurrentUser()?.preferredLanguage;
    if (stored && isSupportedLanguage(stored)) {
      return stored;
    }

    return this.detectBrowserLanguage() ?? DEFAULT_LANGUAGE;
  }

  private detectBrowserLanguage(): Language | null {
    const candidates = navigator.languages?.length ? navigator.languages : [navigator.language];
    for (const tag of candidates) {
      const normalized = this.normalizeToLanguage(tag);
      if (normalized) {
        return normalized;
      }
    }
    return null;
  }

  private normalizeToLanguage(bcp47Tag: string): Language | null {
    const primarySubtag = bcp47Tag.split('-')[0];
    if (!primarySubtag) {
      return null;
    }

    const pascalCased = primarySubtag.charAt(0).toUpperCase() + primarySubtag.slice(1).toLowerCase();
    return isSupportedLanguage(pascalCased) ? pascalCased : null;
  }

  setLanguage(language: Language): Observable<void> {
    const persist$ = this.authService.isLoggedIn()
      ? this.http.patch(`${environment.apiUrl}${environment.endpoints.users}/language`, { language }).pipe(
          map(() => void 0),
          catchError(() => of(void 0))
        )
      : of(void 0);

    return persist$.pipe(switchMap(() => this.loadLanguage(language)));
  }

  private loadLanguage(language: Language): Observable<void> {
    return this.http.get<TranslationCatalogue>(`${this.apiUrl}/${language}`).pipe(
      tap(catalogue => {
        this.catalogueSignal.set(catalogue);
        this.languageSignal.set(language);
      }),
      map(() => void 0),
      catchError(() => {
        this.languageSignal.set(language);
        return of(void 0);
      })
    );
  }

  translate(scope: string, key: string, ...args: (string | number)[]): string {
    const template = this.catalogueSignal()[scope]?.[key] ?? key;
    return args.length === 0
      ? template
      : template.replace(/\{(\d+)\}/g, (match, index) => args[+index] !== undefined ? String(args[+index]) : match);
  }
}
