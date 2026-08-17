import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

export interface RegisterResponse {
  message: string;
  token?: string;
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

// Mirrors the backend SyncApp26.Domain.Enums.Roles constants exactly. A user can hold any
// combination of these (and custom roles an admin created) at once - roles are no longer a single
// value, so there's no enum to switch on.
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
    token: string;
    message: string;
    user: User;
}

export interface ErrorResponse {
  message: string;
}

export interface MessageResponse {
  message: string;
}

@Injectable({
  providedIn: 'root'
})
export class AuthenticationService {
  private apiUrl = environment.apiUrl + '/authentication';

  constructor(private http: HttpClient) {}

  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.apiUrl}/register`, request);
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, request)
      .pipe(tap(response => this.storeSession(response)));
  }

  googleLogin(idToken: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/google-login`, { idToken })
      .pipe(tap(response => this.storeSession(response)));
  }

  microsoftLogin(idToken: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/microsoft-login`, { idToken })
      .pipe(tap(response => this.storeSession(response)));
  }

  private storeSession(response: LoginResponse): void {
    if (response.token) {
      localStorage.setItem('authToken', response.token);
    }
    if (response.user) {
      localStorage.setItem('currentUser', JSON.stringify(response.user));
    }
    // A fresh login always ends any impersonation. Not injecting ImpersonationService here (it would
    // create a cycle through HttpClient) - these are its two stash keys, inlined.
    localStorage.removeItem('impersonationOriginalToken');
    localStorage.removeItem('impersonationOriginalUser');
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${this.apiUrl}/forgot-password`, request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${this.apiUrl}/reset-password`, request);
  }

  logout(): void {
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    // Must also clear any stashed impersonation session - otherwise a different person logging in
    // afterward on the same machine would see the "Return to my account" banner and could hand
    // themselves the previous admin's still-valid token. See ImpersonationService for the key names.
    localStorage.removeItem('impersonationOriginalToken');
    localStorage.removeItem('impersonationOriginalUser');
    // Full reload, not router navigation: root services cache the session's data and
    // nothing resets them, so the next account would see the previous one's.
    window.location.href = '/login';
  }

  getCurrentUser(): User | null {
    const userStr = localStorage.getItem('currentUser');
    return userStr ? JSON.parse(userStr) : null;
  }

  // JwtSecurityTokenHandler's default outbound claim map rewrites ClaimTypes.Role down to the short
  // "role" name when it serializes the token - verified against a live token, the payload carries
  // "role", not the long ClaimTypes URI. Checked as a fallback in case that mapping is ever disabled.
  // Present as a single string when the user holds one role, and as an array when they hold several.
  private static readonly ROLE_CLAIM = 'role';
  private static readonly ROLE_CLAIM_LONG = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

  // Roles and session validity must come from the signed token, not from currentUser in localStorage -
  // that JSON is plain, unsigned storage a user can edit in devtools to grant themselves any role
  // client-side. The token itself still gets rejected server-side, but guards need to reflect that
  // before the API call ever happens, so they read the same source of truth.
  private decodeToken(): Record<string, unknown> | null {
    const token = localStorage.getItem('authToken');
    if (!token) return null;
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    try {
      const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      return JSON.parse(atob(base64));
    } catch {
      return null;
    }
  }

  private getRolesFromToken(): string[] {
    const payload = this.decodeToken();
    const raw = payload?.[AuthenticationService.ROLE_CLAIM] ?? payload?.[AuthenticationService.ROLE_CLAIM_LONG];
    if (!raw) return [];
    return Array.isArray(raw) ? raw : [raw as string];
  }

  isLoggedIn(): boolean {
    const payload = this.decodeToken();
    const exp = payload?.['exp'];
    return typeof exp === 'number' && exp * 1000 > Date.now();
  }

  hasRole(name: string): boolean {
    return this.getRolesFromToken().includes(name);
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
}
