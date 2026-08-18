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
import { User, UserRole, BLOOD_TYPE_LABELS, BLOOD_TYPE_OPTIONS } from '../../models/csv-sync.model';
import { formatDate as formatDateUtil, getRelativeTime as getRelativeTimeUtil } from '../../shared/utils/date-format.util';
import { getRoleBadgeColor as getRoleBadgeColorUtil } from '../../shared/utils/role.util';
import { isValidName, isValidFunction, NAME_ERROR_MESSAGE, FUNCTION_ERROR_MESSAGE } from '../../shared/utils/name-validation.util';
import { CanvasSignaturePad } from '../../shared/utils/canvas-signature-pad';
import { PaginationComponent } from '../pagination/pagination.component';
import { DocumentPageState, DocumentListPageResponse, emptyDocumentPageState } from '../../shared/models/document-page.model';

@Component({
  selector: 'app-basic-user',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './basic-user.component.html',
  styleUrls: ['./basic-user.component.css']
})
export class BasicUserComponent implements OnInit {
  user: User | null = null;
  isLoading = true;
  errorMessage = '';

  // Each of the 6 mini-lists is paginated server-side and fetched independently — see
  // loadPendingSignatures() / the 6 load*Signatures() methods below.
  pendingUser: DocumentPageState = emptyDocumentPageState(5);
  pendingManager: DocumentPageState = emptyDocumentPageState(5);
  pendingInstructor: DocumentPageState = emptyDocumentPageState(5);
  signedUser: DocumentPageState = emptyDocumentPageState(5);
  signedManager: DocumentPageState = emptyDocumentPageState(5);
  signedInstructor: DocumentPageState = emptyDocumentPageState(5);

  onPendingUserPageChange(page: number): void { this.loadPendingUserSignatures(page); }
  onPendingManagerPageChange(page: number): void { this.loadPendingManagerSignatures(page); }
  onPendingInstructorPageChange(page: number): void { this.loadPendingInstructorSignatures(page); }
  onSignedUserPageChange(page: number): void { this.loadSignedUserSignatures(page); }
  onSignedManagerPageChange(page: number): void { this.loadSignedManagerSignatures(page); }
  onSignedInstructorPageChange(page: number): void { this.loadSignedInstructorSignatures(page); }

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
  private sigPad = new CanvasSignaturePad();
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
  bloodTypeLabels = BLOOD_TYPE_LABELS;

  // ── Data Change Request ────────────────────────────────────────────────
  showDataChangeModal = false;
  isSubmittingDataChange = false;
  dataChangeReason = '';
  dataChangeError = '';
  dataChangeSuccess = '';
  
  availableDepartments: string[] = [];
  
  availableFields: { key: string, label: string, type: 'text' | 'date' | 'email' | 'select', options?: { value: string, label: string }[] }[] = [
    { key: 'LastName', label: 'Last Name', type: 'text' },
    { key: 'FirstName', label: 'First Name', type: 'text' },
    { key: 'DateOfBirth', label: 'Date of Birth', type: 'date' },
    { key: 'PlaceOfBirth', label: 'Place of Birth', type: 'text' },
    { key: 'Department', label: 'Department (Name)', type: 'select' },
    { key: 'Function', label: 'Function (Name)', type: 'text' },
    { key: 'Address', label: 'Address', type: 'text' },
    { key: 'BadgeNumber', label: 'Badge Number', type: 'text' },
    { key: 'BloodType', label: 'Blood Type', type: 'select', options: BLOOD_TYPE_OPTIONS }
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
    this.loadPendingUserSignatures();
    this.loadPendingManagerSignatures();
    this.loadPendingInstructorSignatures();
    this.loadSignedUserSignatures();
    // Predates pagination, unrelated to it: manager-signed-documents is only meaningful for line
    // managers, so it's the one load call gated by role (manager-pending-signatures is not).
    if (this.user?.role === UserRole.LineManager) {
      this.loadSignedManagerSignatures();
    }
    this.loadSignedInstructorSignatures();
  }

  private loadDocumentPage(endpoint: string, target: DocumentPageState, page: number,
    assign: (state: DocumentPageState) => void, errorLabel: string): void {
    const params = { page, pageSize: target.pageSize };
    this.http.get<DocumentListPageResponse>(`${environment.apiUrl}/Document/${endpoint}`, { params }).subscribe({
      next: (res) => assign({ ...target, items: res.items, totalCount: res.totalCount, page }),
      error: (err) => console.error(`Failed to load ${errorLabel}`, err)
    });
  }

  // 1. Documents where the user is an employee and needs to sign
  loadPendingUserSignatures(page = 1): void {
    this.loadDocumentPage('my-pending-signatures', this.pendingUser, page, s => this.pendingUser = s, 'pending user signatures');
  }

  // 2. Documents where the user is a manager and needs to sign
  loadPendingManagerSignatures(page = 1): void {
    this.loadDocumentPage('manager-pending-signatures', this.pendingManager, page, s => this.pendingManager = s, 'pending manager signatures');
  }

  // 3. Documents where the user is the linked instructor and needs to sign. Any user can be
  // selected as an instructor regardless of role, so this is fetched unconditionally, same as the
  // manager queue above — it's just empty for anyone not currently an instructor.
  loadPendingInstructorSignatures(page = 1): void {
    this.loadDocumentPage('instructor-pending-signatures', this.pendingInstructor, page, s => this.pendingInstructor = s, 'pending instructor signatures');
  }

  // 4. Documents completed by user
  loadSignedUserSignatures(page = 1): void {
    this.loadDocumentPage('my-signed-documents', this.signedUser, page, s => this.signedUser = s, 'signed user documents');
  }

  // 5. Documents completed by manager
  loadSignedManagerSignatures(page = 1): void {
    this.loadDocumentPage('manager-signed-documents', this.signedManager, page, s => this.signedManager = s, 'signed manager documents');
  }

  // 6. Documents completed as instructor
  loadSignedInstructorSignatures(page = 1): void {
    this.loadDocumentPage('instructor-signed-documents', this.signedInstructor, page, s => this.signedInstructor = s, 'signed instructor documents');
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

    if (
      (actualChanges['FirstName'] && !isValidName(actualChanges['FirstName'])) ||
      (actualChanges['LastName'] && !isValidName(actualChanges['LastName']))
    ) {
      this.dataChangeError = `First/last name: ${NAME_ERROR_MESSAGE}`;
      return;
    }

    if (actualChanges['Function'] && !isValidFunction(actualChanges['Function'])) {
      this.dataChangeError = `Function: ${FUNCTION_ERROR_MESSAGE}`;
      return;
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
