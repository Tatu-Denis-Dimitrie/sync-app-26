import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

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
  admittedByName?: string;
  admittedByFunction?: string;
  admittedDate?: string;
  hireDate?: string;
  createdAt: string;
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
    workplaceTrainingContent: '',
    admittedByName: '',
    admittedByFunction: '',
    admittedDate: ''
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
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
          this.loading = false;
        },
        error: (err) => {
          console.error('Error loading user form:', err);
          this.loading = false;
        }
      });
  }

  populateFormData() {
    if (this.userForm) {
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
        introductoryTrainingDate: this.userForm.introductoryTrainingDate || '',
        introductoryTrainingHours: this.userForm.introductoryTrainingHours || null,
        introductoryTrainingInstructor: this.userForm.introductoryTrainingInstructor || '',
        introductoryTrainingInstructorFunction: this.userForm.introductoryTrainingInstructorFunction || '',
        introductoryTrainingContent: this.userForm.introductoryTrainingContent || '',
        workplaceTrainingDate: this.userForm.workplaceTrainingDate || '',
        workplaceTrainingLocation: this.userForm.workplaceTrainingLocation || '',
        workplaceTrainingHours: this.userForm.workplaceTrainingHours || null,
        workplaceTrainingInstructor: this.userForm.workplaceTrainingInstructor || '',
        workplaceTrainingInstructorFunction: this.userForm.workplaceTrainingInstructorFunction || '',
        workplaceTrainingContent: this.userForm.workplaceTrainingContent || '',
        admittedByName: this.userForm.admittedByName || '',
        admittedByFunction: this.userForm.admittedByFunction || '',
        admittedDate: this.userForm.admittedDate || ''
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

    // Prepare data - convert empty strings to null for date fields
    const dataToSend = {
      ...this.formData,
      dateOfBirth: this.formData.dateOfBirth || null,
      introductoryTrainingDate: this.formData.introductoryTrainingDate || null,
      workplaceTrainingDate: this.formData.workplaceTrainingDate || null,
      admittedDate: this.formData.admittedDate || null
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
