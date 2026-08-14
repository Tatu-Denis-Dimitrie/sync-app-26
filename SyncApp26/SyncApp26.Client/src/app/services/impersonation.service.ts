import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginResponse, Roles, User } from './authentication.service';

// authToken/currentUser are ALWAYS the active session (unchanged semantics - the auth interceptor and
// every guard/role check keep working untouched). These two extra keys are only ever set while
// impersonating, and hold the admin's own session, stashed aside.
const ORIGINAL_TOKEN_KEY = 'impersonationOriginalToken';
const ORIGINAL_USER_KEY = 'impersonationOriginalUser';

@Injectable({ providedIn: 'root' })
export class ImpersonationService {
  private apiUrl = environment.apiUrl + '/authentication';

  private blockedMessageSubject = new BehaviorSubject<string | null>(null);
  blockedMessage$ = this.blockedMessageSubject.asObservable();

  constructor(private http: HttpClient) {}

  isImpersonating(): boolean {
    return !!localStorage.getItem(ORIGINAL_TOKEN_KEY);
  }

  /** The target's identity, read from the live session - null unless currently impersonating. */
  viewingAs(): User | null {
    if (!this.isImpersonating()) return null;
    const userStr = localStorage.getItem('currentUser');
    return userStr ? JSON.parse(userStr) : null;
  }

  /**
   * The admin's own identity, stashed aside for the duration - null unless impersonating. Lets the
   * UI keep showing who you really are while the live session belongs to someone else.
   */
  originalUser(): User | null {
    const userStr = localStorage.getItem(ORIGINAL_USER_KEY);
    return userStr ? JSON.parse(userStr) : null;
  }

  start(userId: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/impersonate/${userId}`, {})
      .pipe(tap(response => {
        // Stash the admin's own session BEFORE overwriting it - order is load-bearing.
        const adminToken = localStorage.getItem('authToken');
        const adminUser = localStorage.getItem('currentUser');
        if (adminToken) localStorage.setItem(ORIGINAL_TOKEN_KEY, adminToken);
        if (adminUser) localStorage.setItem(ORIGINAL_USER_KEY, adminUser);

        localStorage.setItem('authToken', response.token);
        localStorage.setItem('currentUser', JSON.stringify(response.user));

        // Hard reload, not router navigation: root singleton services (30s pending-count polling,
        // SignalR) cache session-scoped state and nothing resets them on a soft navigation - see
        // AuthenticationService.logout() for the same reasoning.
        window.location.href = this.landingRouteFor(response.user.roles);
      }));
  }

  /** Restores the admin's own session. If nothing was stashed, falls back to a clean login. */
  stop(): void {
    const token = localStorage.getItem(ORIGINAL_TOKEN_KEY);
    const user = localStorage.getItem(ORIGINAL_USER_KEY);
    this.clearStashedKeys();

    if (!token || !user) {
      window.location.href = '/login';
      return;
    }

    localStorage.setItem('authToken', token);
    localStorage.setItem('currentUser', user);
    window.location.href = '/dashboard';
  }

  /** Every fresh login (or logout) ends any impersonation - called from AuthenticationService. */
  clearStashedKeys(): void {
    localStorage.removeItem(ORIGINAL_TOKEN_KEY);
    localStorage.removeItem(ORIGINAL_USER_KEY);
  }

  /** Surfaces a 403 IMPERSONATION_READ_ONLY message in the banner for a few seconds. */
  reportBlockedAction(message: string): void {
    this.blockedMessageSubject.next(message);
    setTimeout(() => this.blockedMessageSubject.next(null), 5000);
  }

  private landingRouteFor(roles: string[]): string {
    // Mirrors loading-screen.component.ts. The Admin branch there is unreachable here: the server
    // refuses to issue an impersonation token for an Admin target.
    if (roles.includes(Roles.LineManager)) return '/line-manager';
    return '/basic-user';
  }
}
