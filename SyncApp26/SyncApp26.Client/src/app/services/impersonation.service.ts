import { DOCUMENT } from '@angular/common';
import { Inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthenticationService, Roles, User } from './authentication.service';

interface ImpersonateResponse {
  message: string;
  user: User;
  impersonating: boolean;
}

@Injectable({ providedIn: 'root' })
export class ImpersonationService {
  private apiUrl = environment.apiUrl + '/authentication';

  private blockedMessageSubject = new BehaviorSubject<string | null>(null);
  blockedMessage$ = this.blockedMessageSubject.asObservable();

  constructor(
    private http: HttpClient,
    private authService: AuthenticationService,
    @Inject(DOCUMENT) private document: Document
  ) {}

  isImpersonating(): boolean {
    return this.authService.isImpersonating();
  }

  /** The target's identity, read from the live session - null unless currently impersonating. */
  viewingAs(): User | null {
    return this.isImpersonating() ? this.authService.getCurrentUser() : null;
  }

  /**
   * The admin's own identity - null unless impersonating. Lets the UI keep showing who you really
   * are while the live session belongs to someone else.
   */
  originalUser(): User | null {
    return this.authService.impersonator();
  }

  start(userId: string): Observable<ImpersonateResponse> {
    return this.http.post<ImpersonateResponse>(`${this.apiUrl}/impersonate/${userId}`, {})
      .pipe(tap(response => {
        // Hard reload, not router navigation: root singleton services (pending-count polling,
        // SignalR) cache session-scoped state and nothing resets them on a soft navigation. The
        // reload re-runs the app initializer, which re-fetches /me and picks up the impersonation
        // cookie this response already set - including the impersonator block, which this response
        // alone doesn't carry (only /me does that lookup).
        this.document.location.href = this.landingRouteFor(response.user.roles);
      }));
  }

  /** Restores the admin's own session server-side. Falls back to a clean login if that fails. */
  stop(): void {
    this.http.post(`${this.apiUrl}/stop-impersonation`, {}).subscribe({
      next: () => { this.document.location.href = '/dashboard'; },
      error: () => { this.document.location.href = '/login'; }
    });
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
