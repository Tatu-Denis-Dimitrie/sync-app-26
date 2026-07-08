import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSyncService } from '../../services/user-sync.service';
import { UserSignatureService, UserSignature, UserSignatureHistory } from '../../services/user-signature.service';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { User, UserRole } from '../../models/csv-sync.model';
import { formatDate as formatDateUtil, getRelativeTime as getRelativeTimeUtil } from '../../shared/utils/date-format.util';
import { getRoleBadgeColor as getRoleBadgeColorUtil } from '../../shared/utils/role.util';

@Component({
  selector: 'app-basic-user',
  standalone: true,
  imports: [CommonModule, FormsModule],
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

  // ── Saved Signature ──────────────────────────────────────────────────────
  savedSignature: UserSignature | null = null;
  signatureHistory: UserSignatureHistory[] = [];
  isSigLoading = false;
  sigSuccessMessage = '';
  sigErrorMessage = '';
  showSigHistory = false;

  // Pad state
  sigMode: 'draw' | 'type' = 'draw';
  typedSig = '';
  isSigConfirmed = false;
  private isSigDrawing = false;
  private sigCtx: CanvasRenderingContext2D | null = null;
  private sigLastX = 0;
  private sigLastY = 0;
  private _sigCanvasRef?: ElementRef<HTMLCanvasElement>;

  @ViewChild('sigCanvas')
  set sigCanvasRef(ref: ElementRef<HTMLCanvasElement> | undefined) {
    this._sigCanvasRef = ref;
    if (ref && this.sigMode === 'draw') {
      this.initSigCanvas();
    }
  }

  get sigCanvasRef(): ElementRef<HTMLCanvasElement> | undefined {
    return this._sigCanvasRef;
  }

  UserRole = UserRole;

  // ── Data Change Request ────────────────────────────────────────────────
  showDataChangeModal = false;
  isSubmittingDataChange = false;
  dataChangeReason = '';
  dataChangeError = '';
  dataChangeSuccess = '';
  
  availableDepartments: string[] = [];
  
  availableFields: { key: string, label: string, type: 'text' | 'date' | 'email' | 'select' }[] = [
    { key: 'LastName', label: 'Last Name', type: 'text' },
    { key: 'FirstName', label: 'First Name', type: 'text' },
    { key: 'Department', label: 'Department (Name)', type: 'select' },
    { key: 'Function', label: 'Function (Name)', type: 'text' }
  ];
  selectedFieldKey = '';
  newFieldValue = '';
  requestedChanges: { [key: string]: string } = {};

  get hasRequestedChanges(): boolean {
    return Object.keys(this.requestedChanges).length > 0;
  }
  
  // ────────────────────────────────────────────────────────────────────────

  constructor(
    private authService: AuthenticationService,
    private userSyncService: UserSyncService,
    private userSignatureService: UserSignatureService,
    private dataChangeRequestService: DataChangeRequestService,
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
    this.loadSavedSignature();
    this.loadDepartments();
  }

  loadDepartments(): void {
    this.userSyncService.getDepartments().subscribe({
      next: (depts) => {
        const currentDept = this.user?.departmentName;
        this.availableDepartments = depts
          .filter(d => d.isActive && d.name !== currentDept)
          .map(d => d.name)
          .sort((a, b) => a.localeCompare(b));
      },
      error: (err) => console.error('Failed to load departments', err)
    });
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

  // ── Saved-signature methods ───────────────────────────────────────────────

  loadSavedSignature(): void {
    this.userSignatureService.getMySignature().subscribe({
      next: (sig) => { this.savedSignature = sig; },
      error: () => { this.savedSignature = null; }  // 404 = no saved sig, that's fine
    });
  }

  setSigMode(mode: 'draw' | 'type'): void {
    this.sigMode = mode;
    this.isSigConfirmed = false;
  }

  initSigCanvas(): void {
    const canvas = this.sigCanvasRef?.nativeElement;
    if (!canvas) return;
    this.sigCtx = canvas.getContext('2d');
    if (this.sigCtx) {
      this.sigCtx.lineWidth = 2.5;
      this.sigCtx.lineCap = 'round';
      this.sigCtx.lineJoin = 'round';
      this.sigCtx.strokeStyle = '#0f766e';
    }
  }

  sigStartDrawing(e: MouseEvent | TouchEvent): void {
    if (!this.sigCtx) return;
    this.isSigDrawing = true;
    const { x, y } = this.getSigCoords(e);
    this.sigLastX = x;
    this.sigLastY = y;
  }

  sigDraw(e: MouseEvent | TouchEvent): void {
    if (!this.isSigDrawing || !this.sigCtx) return;
    e.preventDefault();
    const { x, y } = this.getSigCoords(e);
    this.sigCtx.beginPath();
    this.sigCtx.moveTo(this.sigLastX, this.sigLastY);
    this.sigCtx.lineTo(x, y);
    this.sigCtx.stroke();
    this.sigLastX = x;
    this.sigLastY = y;
    this.isSigConfirmed = true;
  }

  sigStopDrawing(): void {
    this.isSigDrawing = false;
  }

  clearSigCanvas(): void {
    const canvas = this.sigCanvasRef?.nativeElement;
    if (!canvas || !this.sigCtx) return;
    this.sigCtx.clearRect(0, 0, canvas.width, canvas.height);
    this.isSigConfirmed = false;
  }

  private getSigCoords(e: MouseEvent | TouchEvent): { x: number; y: number } {
    const canvas = this.sigCanvasRef!.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const sx = canvas.width / rect.width;
    const sy = canvas.height / rect.height;
    if (window.TouchEvent && e instanceof TouchEvent) {
      return { x: (e.touches[0].clientX - rect.left) * sx, y: (e.touches[0].clientY - rect.top) * sy };
    }
    const m = e as MouseEvent;
    return { x: (m.clientX - rect.left) * sx, y: (m.clientY - rect.top) * sy };
  }

  saveSignature(): void {
    if (this.sigMode === 'type' && !this.typedSig.trim()) {
      this.sigErrorMessage = 'Please type your name as your signature.';
      return;
    }
    if (this.sigMode === 'draw' && !this.isSigConfirmed) {
      this.sigErrorMessage = 'Please draw your signature on the pad.';
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

  // ── Data Change Requests ──────────────────────────────────────────────────

  openDataChangeModal(): void {
    this.showDataChangeModal = true;
    this.dataChangeError = '';
    this.dataChangeSuccess = '';
    this.dataChangeReason = '';
    this.requestedChanges = {};
  }

  closeDataChangeModal(): void {
    this.showDataChangeModal = false;
  }

  submitDataChangeRequest(): void {
    const actualChanges: { [key: string]: string } = {};
    for (const key of Object.keys(this.requestedChanges)) {
      if (this.requestedChanges[key] && this.requestedChanges[key].trim() !== '') {
        const val = this.requestedChanges[key].trim();
        actualChanges[key] = val;
      }
    }

    // Validation checks
    if (Object.keys(actualChanges).length === 0) {
      this.dataChangeError = 'Please fill in at least one field to change.';
      return;
    }
    
    if (actualChanges['Email']) {
      const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      if (!emailPattern.test(actualChanges['Email'])) {
        this.dataChangeError = 'Please enter a valid email address.';
        return;
      }
    }

    if (actualChanges['DateOfBirth']) {
      const dob = new Date(actualChanges['DateOfBirth']);
      const today = new Date();
      if (dob > today) {
        this.dataChangeError = 'Date of Birth cannot be in the future.';
        return;
      }
    }
    if (!this.dataChangeReason.trim()) {
      this.dataChangeError = 'Please provide a reason for the change.';
      return;
    }

    this.isSubmittingDataChange = true;
    this.dataChangeError = '';

    const payload = {
      requestedChangesJson: JSON.stringify(actualChanges),
      reason: this.dataChangeReason.trim()
    };

    this.dataChangeRequestService.createRequest(payload).subscribe({
      next: (res) => {
        this.isSubmittingDataChange = false;
        this.dataChangeSuccess = 'Data change request submitted successfully. It is now pending admin approval.';
        this.requestedChanges = {};
        this.dataChangeReason = '';
        setTimeout(() => this.closeDataChangeModal(), 3000);
      },
      error: (err) => {
        this.isSubmittingDataChange = false;
        this.dataChangeError = err.error?.message || 'Failed to submit request.';
      }
    });
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

}
