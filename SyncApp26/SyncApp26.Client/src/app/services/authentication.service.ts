import { DOCUMENT } from '@angular/common';
import { Inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, finalize, map, of, switchMap, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

export interface RegisterResponse {
  message: string;
}

export interface LoginRequest {
    email: string;
    password: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

// Mirrors the backend SyncApp26.Domain.Enums.Roles constants. A user can hold any combination
// of these at once, so there's no single-value enum to switch on.
export const Roles = {
  Admin: 'Admin',
  LineManager: 'LineManager',
  BasicUser: 'BasicUser',
  SsmOfficer: 'SsmOfficer',
  SuOfficer: 'SuOfficer'
} as const;

const KNOWN_ROLE_LABELS: Record<string, string> = {
  [Roles.Admin]: 'Admin',
  [Roles.LineManager]: 'Line Manager',
  [Roles.BasicUser]: 'Basic User',
  [Roles.SsmOfficer]: 'SSM Officer',
  [Roles.SuOfficer]: 'SU Officer'
};

/** Falls back to the raw name for custom roles an admin created, which carry no built-in label. */
export function roleLabel(name: string): string {
  return KNOWN_ROLE_LABELS[name] ?? name;
}

export function rolesLabel(names: string[] | undefined | null): string {
  if (!names || names.length === 0) return '';
  return names.map(roleLabel).join(', ');
}

export interface User {
    id: string;
    email: string;
    firstName: string;
    lastName: string;
    roles: string[];
}

export interface LoginResponse {
    message: string;
    user: User;
}

export interface ErrorResponse {
  message: string;
}

export interface MessageResponse {
  message: string;
}

interface MeResponse {
  authenticated: boolean;
  user?: User;
  impersonating?: boolean;
  impersonator?: User;
}

interface Session {
  user: User;
  impersonating: boolean;
  impersonator: User | null;
}

@Injectable({
  providedIn: 'root'
})
export class AuthenticationService {
  private apiUrl = environment.apiUrl + '/authentication';

  // In-memory only - nothing is read from localStorage anymore.
  private sessionSubject = new BehaviorSubject<Session | null>(null);

  constructor(private http: HttpClient, @Inject(DOCUMENT) private document: Document) {}

  /** Populates session state from the server. Must never throw/reject, or bootstrap aborts with a blank page. */
  hydrate(): Observable<void> {
    return this.http.get<MeResponse>(`${this.apiUrl}/me`).pipe(
      tap(response => this.applyMeResponse(response)),
      map(() => void 0),
      catchError(() => {
        this.sessionSubject.next(null);
        return of(void 0);
      })
    );
  }

  private applyMeResponse(response: MeResponse): void {
    if (!response.authenticated || !response.user) {
      this.sessionSubject.next(null);
      return;
    }
    this.sessionSubject.next({
      user: response.user,
      impersonating: !!response.impersonating,
      impersonator: response.impersonator ?? null
    });
  }

  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.apiUrl}/register`, request);
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, request).pipe(
      tap(response => this.applySessionFromLoginResponse(response)),
      switchMap(response => this.reissueXsrfCookie(response))
    );
  }

  googleLogin(idToken: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/google-login`, { idToken }).pipe(
      tap(response => this.applySessionFromLoginResponse(response)),
      switchMap(response => this.reissueXsrfCookie(response))
    );
  }

  microsoftLogin(idToken: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/microsoft-login`, { idToken }).pipe(
      tap(response => this.applySessionFromLoginResponse(response)),
      switchMap(response => this.reissueXsrfCookie(response))
    );
  }

  private applySessionFromLoginResponse(response: LoginResponse): void {
    this.sessionSubject.next({ user: response.user, impersonating: false, impersonator: null });
  }

  // Login can't issue XSRF-TOKEN itself (still anonymous mid-request, see LoginSuccess), so without
  // this every CSRF-protected request afterward fails: the cookie stays bound to the pre-login identity.
  private reissueXsrfCookie<T>(passthrough: T): Observable<T> {
    return this.http.get(`${this.apiUrl}/me`).pipe(map(() => passthrough));
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${this.apiUrl}/forgot-password`, request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${this.apiUrl}/reset-password`, request);
  }

  logout(): void {
    this.http.post(`${this.apiUrl}/logout`, {}).pipe(
      catchError(() => of(void 0)),
      finalize(() => {
        this.sessionSubject.next(null);
        // Full reload, not router nav: cached session data in root services wouldn't reset otherwise.
        this.document.location.href = '/login';
      })
    ).subscribe();
  }

  getCurrentUser(): User | null {
    return this.sessionSubject.value?.user ?? null;
  }

  isLoggedIn(): boolean {
    return this.sessionSubject.value !== null;
  }

  hasRole(name: string): boolean {
    return this.sessionSubject.value?.user.roles.includes(name) ?? false;
  }

  isAdmin(): boolean {
    return this.hasRole(Roles.Admin);
  }

  isLineManager(): boolean {
    return this.hasRole(Roles.LineManager);
  }

  isSsmOfficer(): boolean {
    return this.hasRole(Roles.SsmOfficer);
  }

  isSuOfficer(): boolean {
    return this.hasRole(Roles.SuOfficer);
  }

  /** Either officer duty - used to gate the shared "stored signature" page. */
  isOfficer(): boolean {
    return this.isSsmOfficer() || this.isSuOfficer();
  }

  // Back ImpersonationService, which has no session state of its own anymore.
  isImpersonating(): boolean {
    return this.sessionSubject.value?.impersonating ?? false;
  }

  impersonator(): User | null {
    return this.sessionSubject.value?.impersonator ?? null;
  }
}
