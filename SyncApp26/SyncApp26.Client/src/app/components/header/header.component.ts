import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { AuthenticationService, User, rolesLabel, Roles } from '../../services/authentication.service';
import { DocumentSignatureService } from '../../services/document-signature.service';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { UserSyncSignalrService, SignatureAnomalyAlert } from '../../services/user-sync.signalr.service';
import { SignatureAnomalyAlertService } from '../../services/signature-anomaly-alert.service';
import { ImpersonationService } from '../../services/impersonation.service';
import { LanguageSwitcherComponent } from '../language-switcher/language-switcher.component';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { filter, Subscription } from 'rxjs';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule, LanguageSwitcherComponent, TranslatePipe],
  templateUrl: './header.component.html',
  styleUrl: './header.component.css'
})
export class HeaderComponent implements OnInit, OnDestroy {
  rolesLabel = (names: string[] | undefined | null): string =>
    rolesLabel(names, (key) => this.tCommon(key));
  currentUser: User | null = null;
  isLoggedIn = false;
  isAdmin = false;
  isLineManager = false;
  isOfficer = false;
  isMenuOpen = false;
  isProfileOpen = false;
  isAnomalyPopoverOpen = false;
  isScrolled = false;
  pendingSignatureCount = 0;
  pendingRequestCount = 0;
  anomalyAlert: SignatureAnomalyAlert | null = null;
  isImpersonating = false;
  /** The admin's own account while impersonating; null otherwise. */
  impersonatorUser: User | null = null;
  impersonationBlockedMessage: string | null = null;
  private routerSubscription!: Subscription;
  private signatureCountSubscription!: Subscription;
  private anomalyAlertSubscription!: Subscription;
  private requestCountSubscription!: Subscription;
  private impersonationBlockedMessageSubscription!: Subscription;

  constructor(
    private authService: AuthenticationService,
    private router: Router,
    private documentSignatureService: DocumentSignatureService,
    private dataChangeRequestService: DataChangeRequestService,
    private signalrService: UserSyncSignalrService,
    private signatureAnomalyAlertService: SignatureAnomalyAlertService,
    private impersonationService: ImpersonationService,
    private translationService: TranslationService
  ) { }

  tCommon(key: string, ...args: (string | number)[]): string {
    return this.translationService.translate('Common', key, ...args);
  }

  ngOnInit(): void {
    this.checkAuthStatus();

    // Unconditional: start()/stop() always hard-reload the page (see ImpersonationService), so there's
    // no stale-flag risk here the way there is for the role-gated subscriptions below.
    this.impersonationBlockedMessageSubscription = this.impersonationService.blockedMessage$.subscribe(
      message => this.impersonationBlockedMessage = message
    );

    // Close menus on navigation
    this.routerSubscription = this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe(() => {
      this.isMenuOpen = false;
      this.isProfileOpen = false;
      this.isAnomalyPopoverOpen = false;
      this.checkAuthStatus();
      if (this.isOfficer) {
        this.loadPendingSignatureCount();
      }
      if (this.isAdmin) {
        this.dataChangeRequestService.loadPendingCount();
      }
    });

    // Signature integrity alerts stay an admin-only concern; the pending-signature badge now
    // belongs to SSM/SU officers, since admins can no longer sign documents (Faza 2/3 of the roles plan).
    if (this.isAdmin || this.isOfficer) {
      // Start SignalR connection for real-time updates
      this.signalrService.startConnection();
    }

    if (this.isOfficer) {
      this.signatureCountSubscription = this.documentSignatureService.getPendingDocumentsCount$().subscribe(
        count => this.pendingSignatureCount = count
      );
      this.loadPendingSignatureCount();
      this.documentSignatureService.startPollingPendingDocuments(30000);
    }

    if (this.isAdmin) {
      // Seeds the badge from whatever the last sweep persisted, so an admin who logs in after the
      // sweep already fired (and missed the live SignalR push below) still sees it immediately.
      this.signatureAnomalyAlertService.getUnread().subscribe({
        next: alerts => {
          if (alerts.length > 0) {
            const latest = alerts[0];
            this.anomalyAlert = {
              anomaliesFound: latest.anomaliesFound,
              recordsChecked: latest.recordsChecked,
              occurredAt: latest.occurredAt
            };
          }
        },
        error: () => {}
      });

      this.anomalyAlertSubscription = this.signalrService.signatureAnomalyAlert$.subscribe(
        alert => this.anomalyAlert = alert
      );

      // Data change request count (a CSV import auto-resolving a request)
      this.requestCountSubscription = this.dataChangeRequestService.getPendingCount$().subscribe(
        count => this.pendingRequestCount = count
      );
      this.dataChangeRequestService.loadPendingCount();
      this.dataChangeRequestService.startPollingPendingCount(30000);
    }
  }

  ngOnDestroy(): void {
    if (this.routerSubscription) {
      this.routerSubscription.unsubscribe();
    }
    if (this.signatureCountSubscription) {
      this.signatureCountSubscription.unsubscribe();
    }
    if (this.anomalyAlertSubscription) {
      this.anomalyAlertSubscription.unsubscribe();
    }
    if (this.requestCountSubscription) {
      this.requestCountSubscription.unsubscribe();
    }
    if (this.impersonationBlockedMessageSubscription) {
      this.impersonationBlockedMessageSubscription.unsubscribe();
    }
  }

  toggleAnomalyPopover(): void {
    this.isAnomalyPopoverOpen = !this.isAnomalyPopoverOpen;
    if (this.isAnomalyPopoverOpen) {
      this.isProfileOpen = false;
      this.isMenuOpen = false;
    }
  }

  dismissAnomalyAlert(): void {
    this.anomalyAlert = null;
    this.isAnomalyPopoverOpen = false;
    this.signatureAnomalyAlertService.dismissAll().subscribe({ error: () => {} });
  }

  checkAuthStatus(): void {
    this.isLoggedIn = this.authService.isLoggedIn();
    this.currentUser = this.authService.getCurrentUser();
    this.isAdmin = this.authService.isAdmin();
    this.isLineManager = this.authService.isLineManager();
    this.isOfficer = this.authService.isOfficer();
    this.isImpersonating = this.impersonationService.isImpersonating();
    this.impersonatorUser = this.impersonationService.originalUser();
  }

  /**
   * Identity the profile menu speaks for: your own account, even while the live session belongs to
   * someone you're impersonating. `currentUser` stays the impersonated user - every role check and
   * guard keys off that - so this is presentation only.
   */
  get profileUser(): User | null {
    return this.impersonatorUser ?? this.currentUser;
  }

  exitImpersonation(): void {
    this.impersonationService.stop();
  }

  /**
   * Initials colour for the impersonated user, keyed to the colours the users list already uses
   * (purple = line manager, blue = basic user). Lighter (400) shades than the app's usual 500/600
   * on purpose - the avatar's background is the same near-black gradient as the profile avatar, so
   * the mid-tone shades used elsewhere on light backgrounds read as muddy here. Classes are spelled
   * out in full: Tailwind scans this file, so anything built by string interpolation would get
   * purged from the bundle.
   */
  private static readonly ROLE_INITIALS: ReadonlyArray<{ role: string; text: string }> = [
    { role: Roles.Admin, text: 'text-rose-400' },
    { role: Roles.LineManager, text: 'text-purple-400' },
    { role: Roles.SsmOfficer, text: 'text-green-400' },
    { role: Roles.SuOfficer, text: 'text-amber-400' }
  ];

  /** Most privileged role wins, since a user can hold several at once. */
  impersonationInitialsClass(): string {
    const roles = this.currentUser?.roles ?? [];
    const match = HeaderComponent.ROLE_INITIALS.find(accent => roles.includes(accent.role));
    return match?.text ?? 'text-blue-400';
  }

  loadPendingSignatureCount(): void {
    if (this.isOfficer) {
      this.documentSignatureService.loadPendingDocumentsCount();
    }
  }

  toggleMenu(): void {
    this.isMenuOpen = !this.isMenuOpen;
    if (this.isMenuOpen) {
      this.isProfileOpen = false;
      this.isAnomalyPopoverOpen = false;
    }
  }

  toggleProfile(): void {
    this.isProfileOpen = !this.isProfileOpen;
    if (this.isProfileOpen) {
      this.isMenuOpen = false;
      this.isAnomalyPopoverOpen = false;
    }
  }

  logout(): void {
    this.authService.logout();
    this.isLoggedIn = false;
    this.currentUser = null;
    this.isAdmin = false;
    this.isLineManager = false;
    this.isOfficer = false;
    this.isMenuOpen = false;
    this.isProfileOpen = false;
    this.isAnomalyPopoverOpen = false;
  }

  /** Initials for the profile chip - yours, not the impersonated user's. */
  getUserInitials(): string {
    return HeaderComponent.initialsOf(this.profileUser);
  }

  /** Initials for the impersonation lockup - the borrowed identity. */
  impersonatedInitials(): string {
    return HeaderComponent.initialsOf(this.currentUser);
  }

  private static initialsOf(user: User | null): string {
    if (!user) return 'U';
    return (user.firstName?.[0] || '') + (user.lastName?.[0] || '');
  }

  getLogoLink(): string {
    if (this.isAdmin) return '/dashboard';
    if (!this.isLineManager && this.isOfficer) return '/documents';
    if (this.isLoggedIn) return '/basic-user';
    return '/login';
  }

  @HostListener('window:scroll', [])
  onScroll(): void {
    this.isScrolled = window.scrollY > 0;
  }
}
