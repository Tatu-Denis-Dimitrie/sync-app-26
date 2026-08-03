import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { AuthenticationService, User, AuthRole, authRoleLabel } from '../../services/authentication.service';
import { DocumentSignatureService } from '../../services/document-signature.service';
import { UserSyncSignalrService, SignatureAnomalyAlert } from '../../services/user-sync.signalr.service';
import { filter, Subscription } from 'rxjs';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './header.component.html',
  styleUrl: './header.component.css'
})
export class HeaderComponent implements OnInit, OnDestroy {
  AuthRole = AuthRole;
  authRoleLabel = authRoleLabel;
  currentUser: User | null = null;
  isLoggedIn = false;
  isAdmin = false;
  isMenuOpen = false;
  isProfileOpen = false;
  isAnomalyPopoverOpen = false;
  isScrolled = false;
  pendingSignatureCount = 0;
  anomalyAlert: SignatureAnomalyAlert | null = null;
  private routerSubscription!: Subscription;
  private signatureCountSubscription!: Subscription;
  private anomalyAlertSubscription!: Subscription;

  constructor(
    private authService: AuthenticationService,
    private router: Router,
    private documentSignatureService: DocumentSignatureService,
    private signalrService: UserSyncSignalrService
  ) { }

  ngOnInit(): void {
    this.checkAuthStatus();

    // Close menus on navigation
    this.routerSubscription = this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe(() => {
      this.isMenuOpen = false;
      this.isProfileOpen = false;
      this.isAnomalyPopoverOpen = false;
      this.checkAuthStatus();
      if (this.isAdmin) {
        this.loadPendingSignatureCount();
      }
    });

    // Subscribe to pending signature count updates
    if (this.isAdmin) {
      // Start SignalR connection for real-time updates
      this.signalrService.startConnection();
      
      this.signatureCountSubscription = this.documentSignatureService.getPendingDocumentsCount$().subscribe(
        count => this.pendingSignatureCount = count
      );
      this.loadPendingSignatureCount();
      this.documentSignatureService.startPollingPendingDocuments(30000);

      this.anomalyAlertSubscription = this.signalrService.signatureAnomalyAlert$.subscribe(
        alert => this.anomalyAlert = alert
      );
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
  }

  loadPendingSignatureCount(): void {
    if (this.isAdmin) {
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
    if (this.isLoggedIn) return '/basic-user';
    return '/login';
  }

  @HostListener('window:scroll', [])
  onScroll(): void {
    this.isScrolled = window.scrollY > 0;
  }
}
