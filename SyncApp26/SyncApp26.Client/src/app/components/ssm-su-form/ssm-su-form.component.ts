import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

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
  roleName?: string;
  managerFirstName?: string;
  managerLastName?: string;
  managerFunctionName?: string;
  dateOfBirth?: string;
  placeOfBirth?: string;
  address?: string;
  bloodGroup?: string;
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
  imports: [CommonModule, FormsModule],
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

  // Editable form data
  formData = {
    dateOfBirth: '',
    placeOfBirth: '',
    address: '',
    bloodGroup: '',
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

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
    ,
    private sanitizer: DomSanitizer
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
            .subscribe({ next: trainings => { this.trainings = trainings || []; }, error: () => { this.trainings = []; } });
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

  get displayedTrainings(): any[] {
    const start = Math.max(0, this.trainings.length - 5);
    return this.trainings.slice(start);
  }

  getSafeImage(data?: string): SafeUrl | null {
    if (!data) return null;
    const prefixed = data.startsWith('data:') ? data : `data:image/png;base64,${data}`;
    return this.sanitizer.bypassSecurityTrustUrl(prefixed);
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
        bloodGroup: this.userForm.bloodGroup || '',
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

  saveForm() {
    if (!this.userId) return;

    this.saving = true;

    const dataToSend = {
      dateOfBirth: this.formData.dateOfBirth || null,
      placeOfBirth: this.formData.placeOfBirth,
      address: this.formData.address,
      bloodGroup: this.formData.bloodGroup,
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
    this.router.navigate(['/employees', this.userId]);
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
