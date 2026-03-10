import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSyncService } from '../../services/user-sync.service';
import { User, UserRole } from '../../models/csv-sync.model';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-basic-user',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './basic-user.component.html',
  styleUrls: ['./basic-user.component.css']
})
export class BasicUserComponent implements OnInit {
  user: User | null = null;
  isLoading = true;
  errorMessage = '';

  pendingUserSignatures: any[] = [];
  pendingManagerSignatures: any[] = [];
  signedUserSignatures: any[] = [];
  signedManagerSignatures: any[] = [];

  UserRole = UserRole;

  constructor(
    private authService: AuthenticationService,
    private userSyncService: UserSyncService,
    private router: Router,
    private http: HttpClient
  ) { }

  ngOnInit(): void {
    const currentUser = this.authService.getCurrentUser();
    if (!currentUser?.id) {
      this.errorMessage = 'User session not found.';
      this.isLoading = false;
      return;
    }

    this.userSyncService.getUserById(currentUser.id).subscribe({
      next: (user) => {
        this.user = user;
        if (!user) {
          this.errorMessage = 'Could not load user details.';
        }
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Could not load user details.';
        this.isLoading = false;
      }
    });

    this.loadPendingSignatures();
  }

  loadPendingSignatures(): void {
    // 1. Fetch documents where the user is an employee and needs to sign
    this.http.get<any[]>(`${environment.apiUrl}/Document/my-pending-signatures`).subscribe({
      next: (docs) => {
        this.pendingUserSignatures = docs;
      },
      error: (err) => console.error('Failed to load pending user signatures', err)
    });

    // 2. Fetch documents where the user is a manager and needs to sign
    this.http.get<any[]>(`${environment.apiUrl}/Document/manager-pending-signatures`).subscribe({
      next: (docs) => {
        this.pendingManagerSignatures = docs;
      },
      error: (err) => console.error('Failed to load pending manager signatures', err)
    });

    // 3. Fetch documents completed by user
    this.http.get<any[]>(`${environment.apiUrl}/Document/my-signed-documents`).subscribe({
      next: (docs) => {
        this.signedUserSignatures = docs;
      },
      error: (err) => console.error('Failed to load signed user documents', err)
    });

    // 4. Fetch documents completed by manager
    if (this.user?.role === UserRole.LineManager) {
      this.http.get<any[]>(`${environment.apiUrl}/Document/manager-signed-documents`).subscribe({
        next: (docs) => {
          this.signedManagerSignatures = docs;
        },
        error: (err) => console.error('Failed to load signed manager documents', err)
      });
    }
  }

  signDocument(documentId: string): void {
    if (!documentId) return;

    // Call backend to generate a valid token for this user for this document
    this.http.get<any>(`${environment.apiUrl}/Document/token-for-document/${documentId}`).subscribe({
      next: (res) => {
        if (res.token) {
          this.router.navigate(['/sign', res.token]);
        }
      },
      error: (err) => {
        console.error('Error generating token', err);
        alert(err.error?.message || 'Could not initiate signature block.');
      }
    });
  }

  viewDocument(documentId: string): void {
    if (!documentId) return;
    this.http.get(`${environment.apiUrl}/Document/${documentId}/view-pdf`, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: (err) => {
        console.error('Error fetching PDF', err);
        alert('Could not open document. Please try again.');
      }
    });
  }

  logout(): void {
    this.authService.logout();
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    const d = new Date(date);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = String(d.getFullYear()).slice(-2);
    return `${day}/${month}/${year}`;
  }

  getRelativeTime(date: Date | string | undefined): string {
    if (!date) return '';
    const now = new Date().getTime();
    const then = new Date(date).getTime();
    const diff = now - then;

    const seconds = Math.floor(diff / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (minutes > 0) return `${minutes}m ago`;
    return 'just now';
  }

  getRoleBadgeColor(role: UserRole | undefined): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
  }

}
