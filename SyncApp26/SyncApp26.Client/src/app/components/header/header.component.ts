import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { AuthenticationService, User } from '../../services/authentication.service';
import { DocumentSignatureService } from '../../services/document-signature.service';
import { UserSyncSignalrService } from '../../services/user-sync.signalr.service';
import { filter, Subscription } from 'rxjs';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './header.component.html',
  styleUrl: './header.component.css'
})
export class HeaderComponent implements OnInit, OnDestroy {
  currentUser: User | null = null;
  isLoggedIn = false;
  isAdmin = false;
  isMenuOpen = false;
  isProfileOpen = false;
  isScrolled = false;
  pendingSignatureCount = 0;
  private routerSubscription!: Subscription;
  private signatureCountSubscription!: Subscription;

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
    }
  }

  ngOnDestroy(): void {
    if (this.routerSubscription) {
      this.routerSubscription.unsubscribe();
    }
    if (this.signatureCountSubscription) {
      this.signatureCountSubscription.unsubscribe();
    }
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
    if (this.isMenuOpen) this.isProfileOpen = false;
  }

  toggleProfile(): void {
    this.isProfileOpen = !this.isProfileOpen;
    if (this.isProfileOpen) this.isMenuOpen = false;
  }

  logout(): void {
    this.authService.logout();
    this.isLoggedIn = false;
    this.currentUser = null;
    this.isAdmin = false;
    this.isMenuOpen = false;
    this.isProfileOpen = false;
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
