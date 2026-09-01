import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, Observable, combineLatest } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSyncService } from '../../services/user-sync.service';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { WorkSiteService } from '../../services/work-site.service';
import { User, UserRole, BLOOD_TYPE_OPTIONS } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';
import { CustomSelectComponent, SelectOption } from '../../shared/components/custom-select/custom-select.component';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Router } from '@angular/router';
import { UserSignatureService, UserSignature, UserSignatureHistory } from '../../services/user-signature.service';
import { NotificationService } from '../../services/notification.service';
import { formatDate as formatDateUtil, getRelativeTime as getRelativeTimeUtil } from '../../shared/utils/date-format.util';
import { getRoleBadgeColor as getRoleBadgeColorUtil } from '../../shared/utils/role.util';
import { isValidName, isValidFunction } from '../../shared/utils/name-validation.util';
import { CanvasSignaturePad } from '../../shared/utils/canvas-signature-pad';
import { DocumentPageState, DocumentListPageResponse, emptyDocumentPageState } from '../../shared/models/document-page.model';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { OrgNamePipe } from '../../shared/pipes/org-name.pipe';

@Component({
  selector: 'app-line-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent, CustomSelectComponent, TranslatePipe, OrgNamePipe],
  templateUrl: './line-manager.component.html',
  styleUrls: ['./line-manager.component.css']
})
export class LineManagerComponent implements OnInit {
  user: User | null = null;
  isLoading = true;
  errorMessage = '';

  // Each of the 6 mini-lists is paginated server-side and fetched independently — see
  // loadPendingSignatures() / the 6 load*Signatures() methods below.
  pendingUser: DocumentPageState = emptyDocumentPageState(10);
  pendingManager: DocumentPageState = emptyDocumentPageState(10);
  pendingInstructor: DocumentPageState = emptyDocumentPageState(10);
  signedUser: DocumentPageState = emptyDocumentPageState(10);
  signedManager: DocumentPageState = emptyDocumentPageState(10);
  signedInstructor: DocumentPageState = emptyDocumentPageState(10);

  onPendingUserPageChange(page: number): void { this.loadPendingUserSignatures(page); }
  onPendingManagerPageChange(page: number): void { this.loadPendingManagerSignatures(page); }
  onPendingInstructorPageChange(page: number): void { this.loadPendingInstructorSignatures(page); }
  onSignedUserPageChange(page: number): void { this.loadSignedUserSignatures(page); }
  onSignedManagerPageChange(page: number): void { this.loadSignedManagerSignatures(page); }
  onSignedInstructorPageChange(page: number): void { this.loadSignedInstructorSignatures(page); }

  onPendingUserPageSizeChange(size: number): void { this.pendingUser = this.withPageSize(this.pendingUser, size); this.loadPendingUserSignatures(1); }
  onPendingManagerPageSizeChange(size: number): void { this.pendingManager = this.withPageSize(this.pendingManager, size); this.loadPendingManagerSignatures(1); }
  onPendingInstructorPageSizeChange(size: number): void { this.pendingInstructor = this.withPageSize(this.pendingInstructor, size); this.loadPendingInstructorSignatures(1); }
  onSignedUserPageSizeChange(size: number): void { this.signedUser = this.withPageSize(this.signedUser, size); this.loadSignedUserSignatures(1); }
  onSignedManagerPageSizeChange(size: number): void { this.signedManager = this.withPageSize(this.signedManager, size); this.loadSignedManagerSignatures(1); }
  onSignedInstructorPageSizeChange(size: number): void { this.signedInstructor = this.withPageSize(this.signedInstructor, size); this.loadSignedInstructorSignatures(1); }

  // Shared by the six handlers above, so the page size is set the same way everywhere — each
  // handler still picks its own field and reload call.
  private withPageSize(state: DocumentPageState, size: number): DocumentPageState {
    state.pageSize = size;
    return state;
  }

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

  // ── Change Email ────────────────────────────────────────────────────────
  showEmailChangeModal = false;
  isSubmittingEmailChange = false;
  emailChangeLocalPart = '';
  emailChangeReason = '';
  emailChangeError = '';
  emailChangeSuccess = '';

  get emailDomain(): string {
    return this.user?.email?.split('@')[1] || '';
  }

  // ── Data Change Request ────────────────────────────────────────────────
  showDataChangeModal = false;
  isSubmittingDataChange = false;
  dataChangeReason = '';
  dataChangeError = '';
  dataChangeSuccess = '';

  availableDepartments: string[] = [];
  availableWorkSites: string[] = [];
  registeredFunctions: string[] = [];

  // Populated in the constructor body, not here - a field initializer can run before the
  // translationService parameter property is assigned, and these labels need it.
  availableFields: { key: string, label: string, type: 'text' | 'date' | 'email' | 'select', options?: { value: string, label: string }[] }[] = [];
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
    private dataChangeRequestService: DataChangeRequestService,
    private workSiteService: WorkSiteService,
    private http: HttpClient,
    private router: Router,
    private userSignatureService: UserSignatureService,
    private notificationService: NotificationService,
    private translationService: TranslationService
  ) {
    this.availableFields = [
      { key: 'LastName', label: this.tUsers('profile.lastName'), type: 'text' },
      { key: 'FirstName', label: this.tUsers('profile.firstName'), type: 'text' },
      { key: 'DateOfBirth', label: this.tUsers('profile.dateOfBirth'), type: 'date' },
      { key: 'PlaceOfBirth', label: this.tUsers('fields.placeOfBirth'), type: 'text' },
      { key: 'Department', label: this.tUsers('fields.departmentName'), type: 'select' },
      { key: 'Function', label: this.tUsers('fields.functionName'), type: 'select' },
      { key: 'WorkSite', label: this.tUsers('fields.workSiteName'), type: 'select' },
      { key: 'Address', label: this.tUsers('profile.address'), type: 'text' },
      { key: 'BadgeNumber', label: this.tUsers('profile.badgeNumber'), type: 'text' },
      { key: 'BloodType', label: this.tUsers('profile.bloodType'), type: 'select', options: BLOOD_TYPE_OPTIONS },
      { key: 'CommuteRoute', label: this.tUsers('fields.commuteRoute'), type: 'text' },
      { key: 'CommuteDurationMinutes', label: this.tUsers('fields.commuteDurationMinutes'), type: 'text' }
    ];
  }

  tUsers(key: string): string {
    return this.translationService.translate('Users', key);
  }

  tDocuments(key: string): string {
    return this.translationService.translate('Documents', key);
  }

  tRequests(key: string): string {
    return this.translationService.translate('Requests', key);
  }

  tCommon(key: string): string {
    return this.translationService.translate('Common', key);
  }

  tValidation(key: string): string {
    return this.translationService.translate('Validation', key);
  }

  tOrganization(key: string): string {
    return this.translationService.translate('Organization', key);
  }

  documentTypeFileLabel(documentType: string): string {
    return this.translationService.translate('Documents', 'labels.documentTypeFile', documentType);
  }

  documentTypeFileSubordinateLabel(documentType: string): string {
    return this.translationService.translate('Documents', 'labels.documentTypeFileSubordinate', documentType);
  }

  documentTypeFileTrainingLabel(documentType: string): string {
    return this.translationService.translate('Documents', 'labels.documentTypeFileTraining', documentType);
  }

  signedByYouOnLabel(date: string): string {
    return this.translationService.translate('Documents', 'messages.signedByYouOn', date);
  }

  countersignedByYouOnLabel(date: string): string {
    return this.translationService.translate('Documents', 'messages.countersignedByYouOn', date);
  }

  signedAsInstructorOnLabel(date: string): string {
    return this.translationService.translate('Documents', 'messages.signedAsInstructorOn', date);
  }

  signAllPendingLabel(count: number): string {
    return this.translationService.translate('Documents', 'actions.signAllPending', count);
  }

  ngOnInit(): void {
    const currentUser = this.authService.getCurrentUser();
    if (!currentUser?.id) {
      this.errorMessage = this.tUsers('messages.userSessionNotFound');
      this.isLoading = false;
      return;
    }

    this.userSyncService.getUserById(currentUser.id).subscribe({
      next: (user) => {
        this.user = user;
        if (!user) {
          this.errorMessage = this.tUsers('messages.couldNotLoadProfileDetails');
          this.isLoading = false;
          return;
        }

        this.loadDepartmentFunctions(user.departmentId);
        this.setupAssignedUsersStream(user);
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = this.tUsers('messages.couldNotLoadProfileDetails');
        this.isLoading = false;
      }
    });

    this.loadPendingSignatures();
    this.loadSavedSignature();
  }

  loadDepartments(): void {
    this.userSyncService.getDepartments().subscribe({
      next: (depts) => {
        const currentDept = this.user?.departmentName;
        this.availableDepartments = depts
          .filter(d => d.isActive && d.name !== currentDept)
          .map(d => d.name)
          .sort((a, b) => a.localeCompare(b));
        this.setDynamicOptions('Department', this.availableDepartments);
      },
      error: (err) => console.error('Failed to load departments', err)
    });
  }

  loadWorkSites(): void {
    this.workSiteService.getAll().subscribe({
      next: (sites) => {
        const currentWorkSite = this.user?.workSite;
        this.availableWorkSites = sites
          .filter(s => s.isActive && s.name !== currentWorkSite)
          .map(s => s.name)
          .sort((a, b) => a.localeCompare(b));
        this.setDynamicOptions('WorkSite', this.availableWorkSites);
      },
      error: (err) => console.error('Failed to load work sites', err)
    });
  }

  // Option lists for the selects whose values come from a backend registry, kept as
  // stable arrays so the dropdown does not see a new [options] reference every cycle.
  private dynamicOptions: { [key: string]: SelectOption[] } = {};

  private setDynamicOptions(key: string, names: string[]): void {
    this.dynamicOptions[key] = names.map(name => ({ value: name, label: name }));
  }

  optionsFor(field: { key: string, options?: { value: string, label: string }[] }): SelectOption[] {
    return this.dynamicOptions[field.key] ?? field.options ?? [];
  }

  loadFunctions(): void {
    this.userSyncService.getAllFunctionNames().subscribe({
      next: (functions) => {
        const currentFunction = this.user?.function?.trim();
        this.registeredFunctions = functions
          .map(f => f.trim())
          .filter(f => f !== currentFunction)
          .sort((a, b) => a.localeCompare(b));
        this.setDynamicOptions('Function', this.registeredFunctions);
      },
      error: (err) => console.error('Failed to load functions', err)
    });
  }

  loadPendingSignatures(): void {
    this.loadPendingUserSignatures();
    this.loadPendingManagerSignatures();
    this.loadSignedUserSignatures();
    this.loadSignedManagerSignatures();

    // Instructor queue is always empty unless this line manager also holds an SSM/SU officer role.
    if (this.authService.isOfficer()) {
      this.loadPendingInstructorSignatures();
      this.loadSignedInstructorSignatures();
    }
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
        alert(err.error?.message || this.tDocuments('messages.couldNotInitiateSignatureBlock'));
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
        alert(this.tDocuments('messages.couldNotOpenDocument'));
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

  onPageSizeChange(size: number): void {
    this.pageSize = size;
    this.currentPage = 1;
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
      this.sigErrorMessage = this.tDocuments('messages.pleaseTypeYourName');
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
        this.sigSuccessMessage = this.tDocuments('messages.signatureSavedSuccessfully');
        this.isSigConfirmed = false;
        this.typedSig = '';
        if (this.sigMode === 'draw') this.clearSigCanvas();
      },
      error: (err) => {
        this.isSigLoading = false;
        this.sigErrorMessage = err.error?.message || this.tDocuments('messages.failedToSaveSignature');
      }
    });
  }

  revokeSignature(): void {
    if (!confirm(this.tDocuments('messages.confirmRevokeSignature'))) return;
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
        this.sigErrorMessage = err.error?.message || this.tDocuments('messages.failedToRevokeSignature');
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

  // ── Change Email ──────────────────────────────────────────────────────────

  openEmailChangeModal(): void {
    this.showEmailChangeModal = true;
    this.emailChangeError = '';
    this.emailChangeSuccess = '';
    this.emailChangeLocalPart = '';
    this.emailChangeReason = '';
  }

  closeEmailChangeModal(): void {
    this.showEmailChangeModal = false;
  }

  submitEmailChangeRequest(): void {
    const localPart = this.emailChangeLocalPart.trim();
    if (!localPart || /[\s@]/.test(localPart)) {
      this.emailChangeError = this.tUsers('messages.invalidEmailLocalPart');
      return;
    }

    this.isSubmittingEmailChange = true;
    this.emailChangeError = '';

    const newEmail = `${localPart}@${this.emailDomain}`;
    this.dataChangeRequestService.requestEmailChange({
      newEmail,
      reason: this.emailChangeReason.trim() || undefined
    }).subscribe({
      next: () => {
        this.isSubmittingEmailChange = false;
        this.emailChangeSuccess = this.tUsers('messages.emailChangeSubmitted');
        this.emailChangeLocalPart = '';
        this.emailChangeReason = '';
        setTimeout(() => this.closeEmailChangeModal(), 3000);
      },
      error: (err) => {
        this.isSubmittingEmailChange = false;
        this.emailChangeError = err.error?.message || this.tCommon('messages.failedToSubmitRequest');
      }
    });
  }

  // ── Data Change Requests ──────────────────────────────────────────────────

  openDataChangeModal(): void {
    this.showDataChangeModal = true;
    this.dataChangeError = '';
    this.dataChangeSuccess = '';
    this.dataChangeReason = '';
    this.requestedChanges = {};

    // Fetched lazily, once, only when this modal is actually opened — not on every page load.
    if (this.availableDepartments.length === 0) this.loadDepartments();
    if (this.availableWorkSites.length === 0) this.loadWorkSites();
    if (this.registeredFunctions.length === 0) this.loadFunctions();
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
      this.dataChangeError = this.tRequests('messages.pleaseFillOneField');
      return;
    }

    if (actualChanges['Email']) {
      const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      if (!emailPattern.test(actualChanges['Email'])) {
        this.dataChangeError = this.tRequests('messages.pleaseEnterValidEmail');
        return;
      }
    }

    if (actualChanges['DateOfBirth']) {
      const dob = new Date(actualChanges['DateOfBirth']);
      const today = new Date();
      if (dob > today) {
        this.dataChangeError = this.tRequests('messages.dobCannotBeFuture');
        return;
      }
    }

    if (
      (actualChanges['FirstName'] && !isValidName(actualChanges['FirstName'])) ||
      (actualChanges['LastName'] && !isValidName(actualChanges['LastName']))
    ) {
      this.dataChangeError = `${this.tValidation('field.firstLastName')}: ${this.tValidation('name.pattern')}`;
      return;
    }

    if (actualChanges['Function'] && !isValidFunction(actualChanges['Function'])) {
      this.dataChangeError = `${this.tValidation('field.function')}: ${this.tValidation('function.maxLength')}`;
      return;
    }

    if (!this.dataChangeReason.trim()) {
      this.dataChangeError = this.tRequests('messages.pleaseProvideReason');
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
        this.dataChangeSuccess = this.tRequests('messages.dataChangeSubmitted');
        this.requestedChanges = {};
        this.dataChangeReason = '';
        setTimeout(() => this.closeDataChangeModal(), 3000);
      },
      error: (err) => {
        this.isSubmittingDataChange = false;
        this.dataChangeError = err.error?.message || this.tCommon('messages.failedToSubmitRequest');
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

  signAllDocuments(): void {
    // BulkSignDocumentsAsync covers both the Manager and Instructor queues for the caller in one
    // call, so a single "Sign All Pending" entry point works regardless of which queue has items.
    const firstDoc = this.pendingManager.items[0] || this.pendingInstructor.items[0];
    if (!firstDoc?.id) return;
    this.http.get<any>(`${environment.apiUrl}/document/token-for-document/${firstDoc.id}`).subscribe({
      next: (res) => {
        if (res.token) {
          this.router.navigate(['/sign', res.token], { queryParams: { bulk: 'true' } });
        }
      },
      error: (err) => {
        console.error('Error generating token for bulk sign', err);
        alert(err.error?.message || this.tDocuments('messages.couldNotInitiateBulkSigning'));
      }
    });
  }

  notifyUser(user: User, documentType: 'SSM' | 'SU'): void {
    if (confirm(this.translationService.translate('Documents', 'messages.confirmNotifyUser', user.firstName, user.lastName, documentType))) {
      this.notificationService.notifyUser(user.id, documentType).subscribe({
        next: (res) => alert(res.message || this.tDocuments('messages.notificationSent')),
        error: (err) => alert(err.error?.message || this.tDocuments('messages.failedToSendNotification'))
      });
    }
  }

  viewSSMSUForm(user: User): void {
    this.router.navigate(['/employees', user.id, 'ssm-su']);
  }

}
