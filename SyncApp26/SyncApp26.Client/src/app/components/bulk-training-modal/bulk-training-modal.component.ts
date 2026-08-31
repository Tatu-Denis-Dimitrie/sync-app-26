import { Component, ElementRef, EventEmitter, Output, ViewChild } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { SignatureVerificationService } from '../../services/signature-verification.service';
import { AuthenticationService } from '../../services/authentication.service';
import { isValidName, NAME_ERROR_MESSAGE } from '../../shared/utils/name-validation.util';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

// Endpoint cap on SyncApp26.API/Controllers/Documents/SignatureVerificationController.cs (MaxUsersPerRequest).
const MAX_VALIDATION_USERS = 200;

interface ExistingSignaturesValidationSummary {
  total: number;
  valid: number;
  invalid: number;
  chainBroken: number;
  legacy: number;
  checkedUserCount: number;
  truncated: boolean;
}

interface DepartmentOption {
  id: string;
  name: string;
  isActive: boolean;
}

interface UserOption {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  departmentId: string;
  departmentName: string;
}

// The job finishes at 'done' as soon as the documents exist; notification emails continue on the
// server after that, so the client never waits on an email phase.
type GenerationPhase = 'generating' | 'done';

// Poll cadence for the generation job. The status endpoint only reads an in-memory dictionary, so
// polling this often is cheap, and generation runs at roughly 30ms per document — a slower interval
// would make the bar jump in large steps instead of tracking documents as they land.
const GENERATION_POLL_MS = 400;

interface BulkGenerateStatus {
  total: number;
  generated: number;
  skipped: number;
  phase: GenerationPhase;
  emailsSent: number;
  emailsFailed: number;
  emailError: string | null;
  emailsAborted: boolean;
  completed: boolean;
  message: string | null;
  error: string | null;
}

interface BulkTrainingData {
  trainingDate: string;
  durationHours: number | null;
  materialTaught: string;
  verifierName: string;
  documentType: string;
  selectedDepartmentId: string | null;
  applyToAllUsers: boolean;
  selectedUserIds: string[];
}

@Component({
  selector: 'app-bulk-training-modal',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  templateUrl: './bulk-training-modal.component.html',
  styleUrls: ['./bulk-training-modal.component.css']
})
export class BulkTrainingModalComponent {
  @Output() close = new EventEmitter<void>();
  @Output() success = new EventEmitter<void>();
  @ViewChild('modalContent') modalContentRef!: ElementRef<HTMLElement>;

  isVisible = false;
  isSubmitting = false;
  submitted = false;
  submittedCount = 0;
  submittedUserIds: string[] = [];
  submittedDocType = '';
  isGenerating = false;
  // Progress is mirrored straight from the backend job — never incremented or animated locally,
  // so the bar can only ever show documents the server has actually finished writing.
  totalToGenerate = 0;
  generatedCount = 0;
  skippedCount = 0;
  generationPhase: GenerationPhase = 'generating';
  private pollTimeoutId: ReturnType<typeof setTimeout> | null = null;
  errorMessage = '';
  validationMessage = '';
  pastDateWarning = false;
  departments: DepartmentOption[] = [];
  isLoadingDepartments = false;
  users: UserOption[] = [];
  isLoadingUsers = false;
  isUserPickerVisible = false;
  userSearchQuery = '';
  pickerDepartmentId: string | null = null;
  pickerShowSelectedOnly = false;

  isValidatingExistingSignatures = false;
  existingSignaturesValidation: ExistingSignaturesValidationSummary | null = null;
  existingSignaturesValidationError = '';

  formData: BulkTrainingData = {
    trainingDate: '',
    durationHours: null,
    materialTaught: '',
    verifierName: '',
    documentType: 'Both',
    selectedDepartmentId: null,
    applyToAllUsers: true,
    selectedUserIds: []
  };

  constructor(
    private http: HttpClient,
    private signatureVerificationService: SignatureVerificationService,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) {
  }

  tDocuments(key: string): string {
    return this.translationService.translate('Documents', key);
  }

  private currentUserFullName(): string {
    const user = this.authService.getCurrentUser();
    return user ? `${user.firstName} ${user.lastName}`.trim() : '';
  }

  private loadDepartments(): void {
    this.isLoadingDepartments = true;
    this.http
      .get<DepartmentOption[]>(`${environment.apiUrl}/Department`)
      .subscribe({
        next: (departments) => {
          this.departments = (departments || [])
            .filter((d) => d.isActive)
            .sort((a, b) => a.name.localeCompare(b.name));
          this.isLoadingDepartments = false;
        },
        error: (err) => {
          console.error('Error loading departments:', err);
          this.departments = [];
          this.isLoadingDepartments = false;
        }
      });
  }

  private loadUsers(): void {
    this.isLoadingUsers = true;
    this.http
      .get<UserOption[]>(`${environment.apiUrl}/User`)
      .subscribe({
        next: (users) => {
          this.users = (users || []).sort((a, b) => {
            const aName = `${a.firstName} ${a.lastName}`.trim();
            const bName = `${b.firstName} ${b.lastName}`.trim();
            return aName.localeCompare(bName);
          });
          this.isLoadingUsers = false;
        },
        error: (err) => {
          console.error('Error loading users:', err);
          this.users = [];
          this.isLoadingUsers = false;
        }
      });
  }

  get filteredUsers(): UserOption[] {
    const query = this.userSearchQuery.trim().toLowerCase();

    return this.users.filter((user) => {
      if (this.pickerShowSelectedOnly && !this.isUserSelected(user.id)) {
        return false;
      }

      const matchesDepartment = !this.pickerDepartmentId || user.departmentId === this.pickerDepartmentId;
      if (!matchesDepartment) {
        return false;
      }

      if (!query) {
        return true;
      }

      const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
      return fullName.includes(query)
        || user.email.toLowerCase().includes(query)
        || (user.departmentName || '').toLowerCase().includes(query);
    });
  }

  get selectedUsersCount(): number {
    return this.formData.selectedUserIds.length;
  }

  onDepartmentChanged(): void {
    // Department filter only affects the user picker display; existing selections are preserved.
  }

  openUserPicker(): void {
    this.isUserPickerVisible = true;
    this.userSearchQuery = '';
    this.pickerDepartmentId = this.formData.selectedDepartmentId;
    this.pickerShowSelectedOnly = false;

    if (!this.users.length) {
      this.loadUsers();
    }
  }

  closeUserPicker(): void {
    this.isUserPickerVisible = false;
  }

  isUserSelected(userId: string): boolean {
    return this.formData.selectedUserIds.includes(userId);
  }

  toggleUserSelection(userId: string): void {
    if (this.isUserSelected(userId)) {
      this.formData.selectedUserIds = this.formData.selectedUserIds.filter((id) => id !== userId);
      return;
    }

    this.formData.selectedUserIds = [...this.formData.selectedUserIds, userId];
  }

  selectAllFilteredUsers(): void {
    const filteredIds = this.filteredUsers.map((u) => u.id);
    const selected = new Set(this.formData.selectedUserIds);
    filteredIds.forEach((id) => selected.add(id));
    this.formData.selectedUserIds = [...selected];
  }

  deselectAllFilteredUsers(): void {
    const filteredIds = new Set(this.filteredUsers.map((u) => u.id));
    this.formData.selectedUserIds = this.formData.selectedUserIds.filter((id) => !filteredIds.has(id));
  }

  onDocumentTypeChanged(): void {
    this.formData.durationHours = this.formData.documentType === 'SU' ? 1 : 2;
  }

  onTrainingDateChanged(): void {
    if (!this.formData.trainingDate) { this.pastDateWarning = false; return; }
    const today = new Date(); today.setHours(0, 0, 0, 0);
    this.pastDateWarning = new Date(this.formData.trainingDate) < today;
  }

  open() {
    this.isVisible = true;
    // Set default date to today
    this.formData.trainingDate = new Date().toISOString().split('T')[0];
    // Set default duration based on current document type
    this.formData.durationHours = this.formData.documentType === 'SU' ? 1 : 2;
    // Default the verifier to whoever is creating the training; still freely editable afterward
    this.formData.verifierName = this.currentUserFullName();

    // Reload in case departments changed while modal was closed
    if (!this.departments.length) {
      this.loadDepartments();
    }

    if (!this.users.length) {
      this.loadUsers();
    }
  }

  closeModal() {
    this.isVisible = false;
    this.isUserPickerVisible = false;
    this.resetForm();
    this.close.emit();
  }

  resetForm() {
    this.formData = {
      trainingDate: '',
      durationHours: 2,
      materialTaught: '',
      verifierName: '',
      documentType: 'Both',
      selectedDepartmentId: null,
      applyToAllUsers: true,
      selectedUserIds: []
    };
    this.userSearchQuery = '';
    this.submitted = false;
    this.submittedCount = 0;
    this.submittedUserIds = [];
    this.submittedDocType = '';
    this.isGenerating = false;
    this.stopPollingGeneration();
    this.totalToGenerate = 0;
    this.generatedCount = 0;
    this.skippedCount = 0;
    this.generationPhase = 'generating';
    this.errorMessage = '';
    this.validationMessage = '';
    this.pastDateWarning = false;
    this.isValidatingExistingSignatures = false;
    this.existingSignaturesValidation = null;
    this.existingSignaturesValidationError = '';
  }

  submitBulkTraining() {
    this.validationMessage = '';
    this.errorMessage = '';

    if (!this.formData.trainingDate) {
      this.validationMessage = this.tDocuments('bulkTraining.validation.trainingDateRequired');
      return;
    }
    if (!this.formData.durationHours || this.formData.durationHours <= 0) {
      this.validationMessage = this.tDocuments('bulkTraining.validation.durationRequired');
      return;
    }
    if (!this.formData.materialTaught?.trim()) {
      this.validationMessage = this.tDocuments('bulkTraining.validation.materialRequired');
      return;
    }
    if (this.formData.documentType !== 'SU' && !this.formData.verifierName?.trim()) {
      this.validationMessage = this.tDocuments('bulkTraining.validation.verifierRequired');
      return;
    }
    if (this.formData.verifierName?.trim() && !isValidName(this.formData.verifierName.trim())) {
      this.validationMessage = `Verifier name: ${NAME_ERROR_MESSAGE}`;
      return;
    }
    if (!this.formData.applyToAllUsers && this.formData.selectedUserIds.length === 0) {
      this.validationMessage = this.tDocuments('bulkTraining.validation.selectAtLeastOneUser');
      return;
    }

    this.isSubmitting = true;

    const payload = {
      ...this.formData,
      selectedDepartmentId: this.formData.selectedDepartmentId ?? null
    };

    this.http.post(`${environment.apiUrl}/PeriodicTraining/bulk`, payload)
      .subscribe({
        next: (response: any) => {
          this.isSubmitting = false;
          this.submitted = true;
          this.submittedCount = response.successCount;
          this.submittedUserIds = this.formData.applyToAllUsers ? [] : [...this.formData.selectedUserIds];
          this.submittedDocType = this.formData.documentType;
          this.success.emit();
          setTimeout(() => {
            this.modalContentRef?.nativeElement?.scrollTo({ top: this.modalContentRef.nativeElement.scrollHeight, behavior: 'smooth' });
          }, 50);
          this.validateExistingSignaturesFor(this.affectedUserIds());
        },
        error: (err) => {
          this.isSubmitting = false;
          console.error('Error creating bulk training:', err);
          this.errorMessage = err?.error?.message || this.tDocuments('bulkTraining.errorCreating');
        }
      });
  }

  // Mirrors the department/selection filtering PeriodicTrainingService.BulkCreateAsync applies
  // server-side, so the validation banner checks exactly the employees the submission touched.
  private affectedUserIds(): string[] {
    if (!this.formData.applyToAllUsers) {
      return [...this.formData.selectedUserIds];
    }
    return this.users
      .filter(u => !this.formData.selectedDepartmentId || u.departmentId === this.formData.selectedDepartmentId)
      .map(u => u.id);
  }

  private validateExistingSignaturesFor(userIds: string[]): void {
    this.existingSignaturesValidation = null;
    this.existingSignaturesValidationError = '';

    if (userIds.length === 0) return;

    const truncated = userIds.length > MAX_VALIDATION_USERS;
    const idsToCheck = truncated ? userIds.slice(0, MAX_VALIDATION_USERS) : userIds;

    this.isValidatingExistingSignatures = true;
    this.signatureVerificationService.getVerificationStatusForUsers(idsToCheck).subscribe({
      next: (statusesByUser) => {
        const all = Object.values(statusesByUser).flat();
        this.existingSignaturesValidation = {
          total: all.length,
          valid: all.filter(s => s.status === 'Valid').length,
          invalid: all.filter(s => s.status === 'Invalid').length,
          chainBroken: all.filter(s => s.status === 'ChainBroken').length,
          legacy: all.filter(s => s.status === 'Legacy').length,
          checkedUserCount: idsToCheck.length,
          truncated
        };
        this.isValidatingExistingSignatures = false;
      },
      error: () => {
        this.existingSignaturesValidationError = this.tDocuments('bulkTraining.errorVerifyingSignatures');
        this.isValidatingExistingSignatures = false;
      }
    });
  }

  get generationPercent(): number {
    if (this.totalToGenerate <= 0) return 0;
    const processed = this.generatedCount + this.skippedCount;
    return Math.min(100, Math.round((processed / this.totalToGenerate) * 100));
  }

  get generationPhaseLabel(): string {
    return this.tDocuments('bulkTraining.generatingDocuments');
  }

  generateDocuments() {
    this.isGenerating = true;
    this.errorMessage = '';
    this.generatedCount = 0;
    this.skippedCount = 0;
    this.totalToGenerate = 0;
    this.generationPhase = 'generating';

    const payload = {
      documentType: this.submittedDocType,
      selectedUserIds: this.submittedUserIds.length > 0 ? this.submittedUserIds : null
    };

    this.http.post<any>(`${environment.apiUrl}/Document/bulk-generate-async`, payload)
      .subscribe({
        next: (res) => {
          // No jobId means there was nothing to generate — behave as the old endpoint did.
          if (!res?.jobId) {
            this.isGenerating = false;
            this.closeModal();
            this.success.emit();
            return;
          }
          this.totalToGenerate = res.total ?? 0;
          this.pollGenerationProgress(res.jobId);
        },
        error: (err) => {
          this.isGenerating = false;
          console.error('Error generating documents:', err);
          this.errorMessage = this.tDocuments('bulkTraining.errorGenerating');
        }
      });
  }

  private stopPollingGeneration(): void {
    if (this.pollTimeoutId !== null) {
      clearTimeout(this.pollTimeoutId);
      this.pollTimeoutId = null;
    }
  }

  private pollGenerationProgress(jobId: string): void {
    this.http.get<BulkGenerateStatus>(`${environment.apiUrl}/Document/bulk-generate-status/${jobId}`)
      .subscribe({
        next: (status) => {
          this.totalToGenerate = status.total;
          this.generatedCount = status.generated;
          this.skippedCount = status.skipped;
          this.generationPhase = status.phase;

          if (!status.completed) {
            this.pollTimeoutId = setTimeout(() => this.pollGenerationProgress(jobId), GENERATION_POLL_MS);
            return;
          }

          this.isGenerating = false;
          if (status.error) {
            this.errorMessage = this.translationService.translate('Documents', 'bulkTraining.errorGeneratingDetail', status.error);
            return;
          }
          // The job reports complete once the documents exist; their notification emails keep
          // sending on the server afterwards, so there is nothing left to wait for here.
          this.closeModal();
          this.success.emit();
        },
        error: () => {
          this.isGenerating = false;
          this.errorMessage = this.tDocuments('bulkTraining.lostJob');
        }
      });
  }

  skipGenerate() {
    this.closeModal();
  }
}
