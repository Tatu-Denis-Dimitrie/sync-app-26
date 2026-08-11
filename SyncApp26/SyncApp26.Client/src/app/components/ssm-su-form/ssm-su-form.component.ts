import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { AuthenticationService } from '../../services/authentication.service';
import { SignatureVerificationService, SignatureVersionSummary, PeriodicTrainingSignatureHistory } from '../../services/signature-verification.service';
import { SignatureStatusBadgeComponent } from '../../components/signature-status-badge/signature-status-badge.component';
import { isValidName, isValidFunction, NAME_ERROR_MESSAGE, FUNCTION_ERROR_MESSAGE } from '../../shared/utils/name-validation.util';
import { BloodType, BLOOD_TYPE_LABELS, BLOOD_TYPE_OPTIONS } from '../../models/csv-sync.model';

interface InitialTrainingEntry {
  documentType: string;
  introductoryTrainingDate?: string;
  introductoryTrainingHours?: number;
  introductoryTrainingInstructor?: string;
  introductoryTrainingInstructorFunction?: string;
  introductoryTrainingContent?: string;
  workplaceTrainingDate?: string;
  workplaceTrainingLocation?: string;
  workplaceTrainingHours?: number;
  workplaceTrainingInstructor?: string;
  workplaceTrainingInstructorFunction?: string;
  workplaceTrainingContent?: string;
}

interface UserSSMSUForm {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  personalId: string;
  departmentName?: string;
  functionName?: string;
  managerFirstName?: string;
  managerLastName?: string;
  managerFunctionName?: string;
  dateOfBirth?: string;
  placeOfBirth?: string;
  address?: string;
  bloodType?: BloodType;
  badgeNumber?: string;
  education?: string;
  qualifications?: string;
  commuteRoute?: string;
  commuteDurationMinutes?: number;
  initialTrainings: InitialTrainingEntry[];
  admittedByName?: string;
  admittedByFunction?: string;
  admittedDate?: string;
  hireDate?: string;
  createdAt: string;
  latestInstructorSignature?: string;
  latestInstructorSignatureMethod?: string;
  latestVerifierSignature?: string;
  latestVerifierSignatureMethod?: string;
}

@Component({
  selector: 'app-ssm-su-form',
  standalone: true,
  imports: [CommonModule, FormsModule, SignatureStatusBadgeComponent],
  templateUrl: './ssm-su-form.component.html',
  styleUrl: './ssm-su-form.component.css'
})
export class SsmSuFormComponent implements OnInit {
  userId: string = '';
  userForm: UserSSMSUForm | null = null;
  loading = true;
  saving = false;
  selectedTab: 'ssm' | 'su' = 'ssm';
  editMode = false;
  generatingDoc = false;
  bloodTypeLabels = BLOOD_TYPE_LABELS;
  bloodTypeOptions = BLOOD_TYPE_OPTIONS;

  // Editable form data
  formData = {
    dateOfBirth: '',
    placeOfBirth: '',
    address: '',
    bloodType: '' as BloodType | '',
    badgeNumber: '',
    education: '',
    qualifications: '',
    commuteRoute: '',
    commuteDurationMinutes: null as number | null,
    admittedByName: '',
    admittedByFunction: '',
    admittedDate: '',
    ssmTraining: {
      introductoryTrainingDate: '',
      introductoryTrainingHours: null as number | null,
      introductoryTrainingInstructor: '',
      introductoryTrainingInstructorFunction: '',
      introductoryTrainingContent: '',
      workplaceTrainingDate: '',
      workplaceTrainingLocation: '',
      workplaceTrainingHours: null as number | null,
      workplaceTrainingInstructor: '',
      workplaceTrainingInstructorFunction: '',
      workplaceTrainingContent: ''
    },
    suTraining: {
      introductoryTrainingDate: '',
      introductoryTrainingHours: null as number | null,
      introductoryTrainingInstructor: '',
      introductoryTrainingInstructorFunction: '',
      introductoryTrainingContent: '',
      workplaceTrainingDate: '',
      workplaceTrainingLocation: '',
      workplaceTrainingHours: null as number | null,
      workplaceTrainingInstructor: '',
      workplaceTrainingInstructorFunction: '',
      workplaceTrainingContent: ''
    }
  };

  // Signature history panel (per periodic training row) — one expanded at a time.
  expandedHistoryTrainingId: string | null = null;
  private historyByTrainingId = new Map<string, SignatureVersionSummary[]>();
  private historyLoadingId: string | null = null;
  private historyErrorByTrainingId = new Map<string, string>();
  // Silent background fetch so hasInvalidSignature() has data before the user ever opens History.
  private historyPrefetchIds = new Set<string>();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private sanitizer: DomSanitizer,
    private authService: AuthenticationService,
    private signatureVerificationService: SignatureVerificationService
  ) {}

  ngOnInit() {
    this.userId = this.route.snapshot.paramMap.get('id') || '';
    if (this.userId) {
      this.loadUserForm();
    }
  }

  loadUserForm() {
    this.loading = true;
    this.http.get<UserSSMSUForm>(`${environment.apiUrl}/user/${this.userId}/ssm-su-form`)
      .subscribe({
        next: (data) => {
          this.userForm = data;
          this.populateFormData();
          // load periodic trainings for this user (to show signatures per-row)
          this.http.get<any[]>(`${environment.apiUrl}/PeriodicTraining/user/${this.userId}`)
            .subscribe({
              next: trainings => { this.trainings = trainings || []; this.prefetchHistoryForDisplayedRows(); },
              error: () => { this.trainings = []; }
            });
          this.loading = false;
        },
        error: (err) => {
          console.error('Error loading user form:', err);
          this.loading = false;
        }
      });
  }

  trainings: any[] = [];

  get latestTraining() {
    return this.trainings && this.trainings.length ? this.trainings[this.trainings.length - 1] : null;
  }

  // Excludes rows carried over from an earlier session (sourceRowId set) — those exist only so a
  // regenerated document's PDF still shows the historical log; they're never independently signed,
  // so showing them here as separate, no-history entries is just noise.
  private displayedTrainingsFor(documentType: 'SSM' | 'SU'): any[] {
    const filtered = this.trainings.filter(t => (t.documentType || '').toUpperCase() === documentType && !t.sourceRowId);
    const start = Math.max(0, filtered.length - 5);
    return filtered.slice(start);
  }

  get displayedSsmTrainings(): any[] {
    return this.displayedTrainingsFor('SSM');
  }

  get displayedSuTrainings(): any[] {
    return this.displayedTrainingsFor('SU');
  }

  getSafeImage(data?: string): SafeUrl | null {
    if (!data) return null;
    const prefixed = data.startsWith('data:') ? data : `data:image/png;base64,${data}`;
    return this.sanitizer.bypassSecurityTrustUrl(prefixed);
  }

  isHistoryExpanded(t: any): boolean {
    return this.expandedHistoryTrainingId === t.id;
  }

  isHistoryLoading(t: any): boolean {
    return this.historyLoadingId === t.id;
  }

  getHistoryError(t: any): string | undefined {
    return this.historyErrorByTrainingId.get(t.id);
  }

  // Combined, chronological (by signedAt) view across all signer roles for one training row.
  getHistoryEntries(t: any): SignatureVersionSummary[] {
    return this.historyByTrainingId.get(t.id) || [];
  }

  toggleHistory(t: any): void {
    if (this.expandedHistoryTrainingId === t.id) {
      this.expandedHistoryTrainingId = null;
      return;
    }

    this.expandedHistoryTrainingId = t.id;
    if (this.historyByTrainingId.has(t.id) || this.historyLoadingId === t.id) return;

    this.historyErrorByTrainingId.delete(t.id);
    this.historyLoadingId = t.id;
    this.signatureVerificationService.getSignatureHistory(t.id).subscribe({
      next: (history) => {
        this.cacheHistoryResponse(t.id, history);
        this.historyLoadingId = null;
      },
      error: () => {
        this.historyErrorByTrainingId.set(t.id, 'Failed to load signature history for this session.');
        this.historyLoadingId = null;
      }
    });
  }

  private cacheHistoryResponse(trainingId: string, history: PeriodicTrainingSignatureHistory): void {
    const entries = Object.values(history.versionsByRole)
      .flat()
      .sort((a, b) => new Date(a.signedAt).getTime() - new Date(b.signedAt).getTime());
    this.historyByTrainingId.set(trainingId, entries);
  }

  // Fires right after trainings load so hasInvalidSignature() has an answer before the user ever
  // expands History. Best-effort: a failed prefetch just means the Exclude-from-print button won't
  // show for that row until History is opened manually — no user-facing error for a background fetch.
  private prefetchHistoryForDisplayedRows(): void {
    const rows = [...this.displayedSsmTrainings, ...this.displayedSuTrainings];
    for (const t of rows) {
      if (this.historyByTrainingId.has(t.id) || this.historyPrefetchIds.has(t.id)) continue;

      this.historyPrefetchIds.add(t.id);
      this.signatureVerificationService.getSignatureHistory(t.id).subscribe({
        next: (history) => this.cacheHistoryResponse(t.id, history),
        error: () => { /* best-effort prefetch — see comment above */ }
      });
    }
  }

  hasInvalidSignature(t: any): boolean {
    return this.getHistoryEntries(t).some(e => e.status === 'Invalid' || e.status === 'ChainBroken');
  }

  isExcludedFromPrint(t: any): boolean {
    return !!t.excludedFromPrintAt;
  }

  get isAdmin(): boolean {
    return this.authService.isAdmin();
  }

  togglePrintExclusion(t: any): void {
    const excluding = !this.isExcludedFromPrint(t);
    const confirmMessage = excluding
      ? 'Exclude this training row from every printed document? It will stay in the system but disappear from PDFs and printouts.'
      : 'Include this training row in printed documents again?';
    if (!confirm(confirmMessage)) return;

    this.http.patch<any>(`${environment.apiUrl}/PeriodicTraining/${t.id}/print-exclusion`, { excluded: excluding })
      .subscribe({
        next: (updated) => { t.excludedFromPrintAt = updated.excludedFromPrintAt; },
        error: (err) => { alert(err.error?.message || 'Failed to update print exclusion.'); }
      });
  }

  populateFormData() {
    if (this.userForm) {
      const fromEntry = (e?: InitialTrainingEntry) => ({
        introductoryTrainingDate: e?.introductoryTrainingDate || '',
        introductoryTrainingHours: e?.introductoryTrainingHours || null,
        introductoryTrainingInstructor: e?.introductoryTrainingInstructor || '',
        introductoryTrainingInstructorFunction: e?.introductoryTrainingInstructorFunction || '',
        introductoryTrainingContent: e?.introductoryTrainingContent || '',
        workplaceTrainingDate: e?.workplaceTrainingDate || '',
        workplaceTrainingLocation: e?.workplaceTrainingLocation || '',
        workplaceTrainingHours: e?.workplaceTrainingHours || null,
        workplaceTrainingInstructor: e?.workplaceTrainingInstructor || '',
        workplaceTrainingInstructorFunction: e?.workplaceTrainingInstructorFunction || '',
        workplaceTrainingContent: e?.workplaceTrainingContent || ''
      });
      const ssmEntry = this.userForm.initialTrainings?.find(t => t.documentType === 'SSM');
      const suEntry = this.userForm.initialTrainings?.find(t => t.documentType === 'SU');
      this.formData = {
        dateOfBirth: this.userForm.dateOfBirth || '',
        placeOfBirth: this.userForm.placeOfBirth || '',
        address: this.userForm.address || '',
        bloodType: this.userForm.bloodType || '',
        badgeNumber: this.userForm.badgeNumber || '',
        education: this.userForm.education || '',
        qualifications: this.userForm.qualifications || '',
        commuteRoute: this.userForm.commuteRoute || '',
        commuteDurationMinutes: this.userForm.commuteDurationMinutes || null,
        admittedByName: this.userForm.admittedByName || '',
        admittedByFunction: this.userForm.admittedByFunction || '',
        admittedDate: this.userForm.admittedDate || '',
        ssmTraining: fromEntry(ssmEntry),
        suTraining: fromEntry(suEntry)
      };
    }
  }

  toggleEditMode() {
    this.editMode = !this.editMode;
    if (!this.editMode) {
      // If canceling edit, reload data
      this.populateFormData();
    }
  }

  private optionalNameValid(value?: string): boolean {
    return !value || isValidName(value);
  }

  private optionalFunctionValid(value?: string): boolean {
    return !value || isValidFunction(value);
  }

  saveForm() {
    if (!this.userId) return;

    const nameFields = [
      this.formData.admittedByName,
      this.formData.ssmTraining.introductoryTrainingInstructor,
      this.formData.ssmTraining.workplaceTrainingInstructor,
      this.formData.suTraining.introductoryTrainingInstructor,
      this.formData.suTraining.workplaceTrainingInstructor
    ];
    if (nameFields.some(v => !this.optionalNameValid(v))) {
      alert(`Instructor/admitted-by name: ${NAME_ERROR_MESSAGE}`);
      return;
    }

    const functionFields = [
      this.formData.admittedByFunction,
      this.formData.ssmTraining.introductoryTrainingInstructorFunction,
      this.formData.ssmTraining.workplaceTrainingInstructorFunction,
      this.formData.suTraining.introductoryTrainingInstructorFunction,
      this.formData.suTraining.workplaceTrainingInstructorFunction
    ];
    if (functionFields.some(v => !this.optionalFunctionValid(v))) {
      alert(`Instructor/admitted-by function: ${FUNCTION_ERROR_MESSAGE}`);
      return;
    }

    this.saving = true;

    const dataToSend = {
      dateOfBirth: this.formData.dateOfBirth || null,
      placeOfBirth: this.formData.placeOfBirth,
      address: this.formData.address,
      bloodType: this.formData.bloodType || null,
      badgeNumber: this.formData.badgeNumber,
      education: this.formData.education,
      qualifications: this.formData.qualifications,
      commuteRoute: this.formData.commuteRoute,
      commuteDurationMinutes: this.formData.commuteDurationMinutes,
      admittedByName: this.formData.admittedByName,
      admittedByFunction: this.formData.admittedByFunction,
      admittedDate: this.formData.admittedDate || null,
      initialTrainings: [
        {
          documentType: 'SSM',
          ...this.formData.ssmTraining,
          introductoryTrainingDate: this.formData.ssmTraining.introductoryTrainingDate || null,
          workplaceTrainingDate: this.formData.ssmTraining.workplaceTrainingDate || null
        },
        {
          documentType: 'SU',
          ...this.formData.suTraining,
          introductoryTrainingDate: this.formData.suTraining.introductoryTrainingDate || null,
          workplaceTrainingDate: this.formData.suTraining.workplaceTrainingDate || null
        }
      ]
    };

    this.http.put(`${environment.apiUrl}/user/${this.userId}/ssm-su-form`, dataToSend)
      .subscribe({
        next: () => {
          this.saving = false;
          this.editMode = false;
          this.loadUserForm(); // Reload to get updated data
          alert('Form saved successfully!');
        },
        error: (err) => {
          console.error('Error saving form:', err);
          this.saving = false;
          alert('Error saving form. Please try again.');
        }
      });
  }

  generateDocument() {
    if (!this.userId) return;

    const type = this.selectedTab === 'ssm' ? 'SSM' : 'SU';

    if (confirm(`Are you sure you want to generate the ${type} document and request signatures? Make sure all data is saved first.`)) {
      this.generatingDoc = true;
      this.http.post(`${environment.apiUrl}/Document/generate`, {
        userId: this.userId,
        documentType: type
      }).subscribe({
        next: () => {
          this.generatingDoc = false;
          alert(`${type} Document generated successfully! An email signature request has been sent to the user.`);
        },
        error: (err) => {
          console.error('Error generating document:', err);
          this.generatingDoc = false;
          alert('Error generating document. Please try again.');
        }
      });
    }
  }

  goBack() {
    if (this.authService.isAdmin()) {
      this.router.navigate(['/employees', this.userId]);
    } else {
      this.router.navigate(['/line-manager']);
    }
  }

  print() {
    this.editMode = false; // Exit edit mode before printing
    setTimeout(() => window.print(), 100);
  }

  getFullName(): string {
    return `${this.userForm?.firstName} ${this.userForm?.lastName}`;
  }

  getManagerFullName(): string {
    return `${this.userForm?.managerFirstName} ${this.userForm?.managerLastName}`;
  }

  formatDate(dateString?: string): string {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleDateString('ro-RO');
  }
}
