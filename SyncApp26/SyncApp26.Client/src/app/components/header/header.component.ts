import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { AuthenticationService, User, rolesLabel } from '../../services/authentication.service';
import { DocumentSignatureService } from '../../services/document-signature.service';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { UserSyncSignalrService, SignatureAnomalyAlert } from '../../services/user-sync.signalr.service';
import { ImpersonationService } from '../../services/impersonation.service';
import { filter, Subscription } from 'rxjs';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './header.component.html',
  styleUrl: './header.component.css'
})
export class HeaderComponent implements OnInit, OnDestroy {
  rolesLabel = rolesLabel;
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
    private impersonationService: ImpersonationService
  ) { }

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
  }

  checkAuthStatus(): void {
    this.isLoggedIn = this.authService.isLoggedIn();
    this.currentUser = this.authService.getCurrentUser();
    this.isAdmin = this.authService.isAdmin();
    this.isLineManager = this.authService.isLineManager();
    this.isOfficer = this.authService.isOfficer();
    this.isImpersonating = this.impersonationService.isImpersonating();
  }

  exitImpersonation(): void {
    this.impersonationService.stop();
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

  getUserInitials(): string {
    if (!this.currentUser) return 'U';
    return (this.currentUser.firstName?.[0] || '') + (this.currentUser.lastName?.[0] || '');
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
