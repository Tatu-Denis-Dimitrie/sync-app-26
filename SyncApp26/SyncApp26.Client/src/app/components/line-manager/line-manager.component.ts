import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, Observable, combineLatest } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSyncService } from '../../services/user-sync.service';
import { User, UserRole } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Router } from '@angular/router';
import { UserSignatureService, UserSignature, UserSignatureHistory } from '../../services/user-signature.service';
import { NotificationService } from '../../services/notification.service';
import { formatDate as formatDateUtil, getRelativeTime as getRelativeTimeUtil } from '../../shared/utils/date-format.util';
import { getRoleBadgeColor as getRoleBadgeColorUtil } from '../../shared/utils/role.util';
import { CanvasSignaturePad } from '../../shared/utils/canvas-signature-pad';

@Component({
  selector: 'app-line-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './line-manager.component.html',
  styleUrls: ['./line-manager.component.css']
})
export class LineManagerComponent implements OnInit {
  user: User | null = null;
  isLoading = true;
  errorMessage = '';

  pendingUserSignatures: any[] = [];
  pendingManagerSignatures: any[] = [];
  signedUserSignatures: any[] = [];
  signedManagerSignatures: any[] = [];

  assignedUsers$!: Observable<User[]>;
  paginatedAssignedUsers$!: Observable<User[]>;
  availableFunctions: string[] = [];

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 10;
  totalItems = 0;

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedFunction$ = new BehaviorSubject<string>('all');

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedFunction(): string { return this.selectedFunction$.value; }
  set selectedFunction(value: string) { this.selectedFunction$.next(value); }

  UserRole = UserRole;

  // ── Signature state ──────────────────────────────────────────────────────
  savedSignature: UserSignature | null = null;
  signatureHistory: UserSignatureHistory[] = [];
  isSigLoading = false;
  sigSuccessMessage = '';
  sigErrorMessage = '';
  showSigHistory = false;
  sigMode: 'draw' | 'type' = 'draw';
  typedSig = '';
  isSigConfirmed = false;
  private sigPad = new CanvasSignaturePad();
  @ViewChild('sigCanvas') sigCanvasRef?: ElementRef<HTMLCanvasElement>;
  // ─────────────────────────────────────────────────────────────────────────

  constructor(
    private authService: AuthenticationService,
    private userSyncService: UserSyncService,
    private http: HttpClient,
    private router: Router,
    private userSignatureService: UserSignatureService,
    private notificationService: NotificationService
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
          this.errorMessage = 'Could not load profile details.';
          this.isLoading = false;
          return;
        }

        this.loadDepartmentFunctions(user.departmentId);
        this.setupAssignedUsersStream(user);
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Could not load profile details.';
        this.isLoading = false;
      }
    });

    this.loadPendingSignatures();
    this.loadSavedSignature();
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
    this.http.get<any[]>(`${environment.apiUrl}/Document/manager-signed-documents`).subscribe({
      next: (docs) => {
        this.signedManagerSignatures = docs;
      },
      error: (err) => console.error('Failed to load signed manager documents', err)
    });
  }

  signDocument(documentId: string): void {
    if (!documentId) return;

    // Call backend to generate a valid token for this user for this document
    this.http.get<any>(`${environment.apiUrl}/document/token-for-document/${documentId}`).subscribe({
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

  private setupAssignedUsersStream(manager: User): void {
    this.assignedUsers$ = this.userSyncService.users$.pipe(
      map(users => users.filter(u =>
        (u.assignedToId === manager.id || u.assignedToPersonalId === manager.personalId) &&
        u.id !== manager.id
      ))
    );

    this.paginatedAssignedUsers$ = combineLatest([
      this.assignedUsers$,
      this.searchQuery$,
      this.selectedFunction$,
      this.currentPage$
    ]).pipe(
      map(([users, searchQuery, selectedFunction, currentPage]) => {
        const query = searchQuery.trim().toLowerCase();

        const filtered = users.filter(user => {
          const matchesSearch = !query ||
            user.firstName.toLowerCase().includes(query) ||
            user.lastName.toLowerCase().includes(query);

          const matchesFunction = selectedFunction === 'all' ||
            (user.function || '').trim().toLowerCase() === selectedFunction.trim().toLowerCase();

          return matchesSearch && matchesFunction;
        });

        this.totalItems = filtered.length;
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  private loadDepartmentFunctions(departmentId: string): void {
    this.userSyncService.getFunctionsByDepartmentId(departmentId)
      .pipe(take(1))
      .subscribe(functions => {
        this.availableFunctions = functions;
      });
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFunctionFilterChange(): void {
    this.currentPage = 1;
  }

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  // ── Saved-signature methods ───────────────────────────────────────────────

  loadSavedSignature(): void {
    this.userSignatureService.getMySignature().subscribe({
      next: (sig) => { this.savedSignature = sig; },
      error: () => { this.savedSignature = null; }
    });
  }

  setSigMode(mode: 'draw' | 'type'): void {
    this.sigMode = mode;
    this.isSigConfirmed = false;
    if (mode === 'draw') setTimeout(() => this.initSigCanvas(), 50);
  }

  initSigCanvas(): void {
    this.sigPad.attach(this.sigCanvasRef?.nativeElement);
  }

  sigStartDrawing(e: MouseEvent | TouchEvent): void {
    this.sigPad.startDrawing(e);
  }

  sigDraw(e: MouseEvent | TouchEvent): void {
    if (this.sigPad.draw(e)) this.isSigConfirmed = true;
  }

  sigStopDrawing(): void {
    this.sigPad.stopDrawing();
  }

  clearSigCanvas(): void {
    this.sigPad.clear();
    this.isSigConfirmed = false;
  }

  saveSignature(): void {
    if (this.sigMode === 'type' && !this.typedSig.trim()) {
      this.sigErrorMessage = 'Please type your name as your signature.';
      return;
    }
    const data = this.sigMode === 'draw'
      ? (this.sigCanvasRef?.nativeElement.toDataURL('image/png') ?? '')
      : this.typedSig;
    const method = this.sigMode === 'draw' ? 'Draw' : 'Type';
    this.isSigLoading = true;
    this.sigErrorMessage = '';
    this.sigSuccessMessage = '';
    this.userSignatureService.saveMySignature({ signatureData: data, signatureMethod: method }).subscribe({
      next: (res) => {
        this.isSigLoading = false;
        this.savedSignature = res.signature;
        this.sigSuccessMessage = 'Signature saved successfully!';
        this.isSigConfirmed = false;
        this.typedSig = '';
        if (this.sigMode === 'draw') this.clearSigCanvas();
      },
      error: (err) => {
        this.isSigLoading = false;
        this.sigErrorMessage = err.error?.message || 'Failed to save signature. Please try again.';
      }
    });
  }

  revokeSignature(): void {
    if (!confirm('Are you sure you want to remove your saved signature? This will be recorded in the audit log.')) return;
    this.isSigLoading = true;
    this.sigErrorMessage = '';
    this.sigSuccessMessage = '';
    this.userSignatureService.revokeMySignature().subscribe({
      next: (res) => {
        this.isSigLoading = false;
        this.savedSignature = null;
        this.sigSuccessMessage = res.message;
      },
      error: (err) => {
        this.isSigLoading = false;
        this.sigErrorMessage = err.error?.message || 'Failed to revoke signature.';
      }
    });
  }

  loadSignatureHistory(): void {
    this.showSigHistory = !this.showSigHistory;
    if (this.showSigHistory && this.signatureHistory.length === 0) {
      this.userSignatureService.getMyHistory().subscribe({
        next: (h) => { this.signatureHistory = h; },
        error: () => {}
      });
    }
  }

  formatDateTime(d: string): string {
    return new Date(d).toLocaleString();
  }

  // ─────────────────────────────────────────────────────────────────────────

  logout(): void {
    this.authService.logout();
  }

  formatDate(date: Date | string | undefined): string {
    return formatDateUtil(date);
  }

  getRelativeTime(date: Date | string | undefined): string {
    return getRelativeTimeUtil(date);
  }

  getRoleBadgeColor(role: UserRole | undefined): string {
    return getRoleBadgeColorUtil(role);
  }

  signAllDocuments(): void {
    const firstDoc = this.pendingManagerSignatures[0];
    if (!firstDoc?.id) return;
    this.http.get<any>(`${environment.apiUrl}/document/token-for-document/${firstDoc.id}`).subscribe({
      next: (res) => {
        if (res.token) {
          this.router.navigate(['/sign', res.token], { queryParams: { bulk: 'true' } });
        }
      },
      error: (err) => {
        console.error('Error generating token for bulk sign', err);
        alert(err.error?.message || 'Could not initiate bulk signing.');
      }
    });
  }

  notifyUser(user: User, documentType: 'SSM' | 'SU'): void {
    if (confirm(`Are you sure you want to notify ${user.firstName} ${user.lastName} about the missing ${documentType} document?`)) {
      this.notificationService.notifyUser(user.id, documentType).subscribe({
        next: (res) => alert(res.message || 'Notification sent!'),
        error: (err) => alert(err.error?.message || 'Failed to send notification.')
      });
    }
  }

  viewSSMSUForm(user: User): void {
    this.router.navigate(['/employees', user.id, 'ssm-su']);
  }

}
