import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

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

interface BulkInitialTrainingData {
  documentType: string;
  introductoryTrainingDate: string;
  introductoryTrainingHours: number | null;
  introductoryTrainingInstructor: string;
  introductoryTrainingInstructorFunction: string;
  introductoryTrainingContent: string;
  workplaceTrainingDate: string;
  jobTitle: string;
  workplaceTrainingHours: number | null;
  workplaceTrainingInstructor: string;
  workplaceTrainingInstructorFunction: string;
  workplaceTrainingContent: string;
  selectedDepartmentId: string | null;
  applyToAllUsers: boolean;
  selectedUserIds: string[];
}

@Component({
  selector: 'app-bulk-initial-training-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bulk-initial-training-modal.component.html',
  styleUrls: ['./bulk-initial-training-modal.component.css']
})
export class BulkInitialTrainingModalComponent implements OnInit {
  @Output() close = new EventEmitter<void>();
  @Output() success = new EventEmitter<void>();

  isVisible = false;
  isSubmitting = false;
  submitted = false;
  submittedCount = 0;
  errorMessage = '';
  validationMessage = '';
  departments: DepartmentOption[] = [];
  isLoadingDepartments = false;
  users: UserOption[] = [];
  isLoadingUsers = false;
  isUserPickerVisible = false;
  userSearchQuery = '';
  pickerDepartmentId: string | null = null;
  pickerShowSelectedOnly = false;

  formData: BulkInitialTrainingData = {
    documentType: 'Both',
    introductoryTrainingDate: '',
    introductoryTrainingHours: null,
    introductoryTrainingInstructor: '',
    introductoryTrainingInstructorFunction: '',
    introductoryTrainingContent: '',
    workplaceTrainingDate: '',
    jobTitle: '',
    workplaceTrainingHours: null,
    workplaceTrainingInstructor: '',
    workplaceTrainingInstructorFunction: '',
    workplaceTrainingContent: '',
    selectedDepartmentId: null,
    applyToAllUsers: true,
    selectedUserIds: []
  };

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadDepartments();
    this.loadUsers();
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

  open() {
    this.isVisible = true;
    const today = new Date().toISOString().split('T')[0];
    this.formData.introductoryTrainingDate = today;
    this.formData.workplaceTrainingDate = today;

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
      documentType: 'Both',
      introductoryTrainingDate: '',
      introductoryTrainingHours: null,
      introductoryTrainingInstructor: '',
      introductoryTrainingInstructorFunction: '',
      introductoryTrainingContent: '',
      workplaceTrainingDate: '',
      jobTitle: '',
      workplaceTrainingHours: null,
      workplaceTrainingInstructor: '',
      workplaceTrainingInstructorFunction: '',
      workplaceTrainingContent: '',
      selectedDepartmentId: null,
      applyToAllUsers: true,
      selectedUserIds: []
    };
    this.userSearchQuery = '';
    this.submitted = false;
    this.submittedCount = 0;
    this.errorMessage = '';
    this.validationMessage = '';
  }

  submitBulkInitialTraining() {
    this.validationMessage = '';
    this.errorMessage = '';

    if (!this.formData.introductoryTrainingDate) {
      this.validationMessage = 'Please provide the introductory training date.';
      return;
    }
    if (!this.formData.introductoryTrainingHours || this.formData.introductoryTrainingHours <= 0) {
      this.validationMessage = 'Please enter the introductory training hours.';
      return;
    }
    if (!this.formData.introductoryTrainingInstructor?.trim()) {
      this.validationMessage = 'Please enter the introductory training instructor name.';
      return;
    }
    if (!this.formData.introductoryTrainingInstructorFunction?.trim()) {
      this.validationMessage = 'Please enter the introductory training instructor function.';
      return;
    }
    if (!this.formData.workplaceTrainingDate) {
      this.validationMessage = 'Please provide the workplace training date.';
      return;
    }
    if (!this.formData.workplaceTrainingHours || this.formData.workplaceTrainingHours <= 0) {
      this.validationMessage = 'Please enter the workplace training hours.';
      return;
    }
    if (!this.formData.jobTitle?.trim()) {
      this.validationMessage = 'Please enter the job title / workplace location.';
      return;
    }
    if (!this.formData.workplaceTrainingInstructor?.trim()) {
      this.validationMessage = 'Please enter the workplace training instructor name.';
      return;
    }
    if (!this.formData.workplaceTrainingInstructorFunction?.trim()) {
      this.validationMessage = 'Please enter the workplace training instructor function.';
      return;
    }
    if (!this.formData.applyToAllUsers && this.formData.selectedUserIds.length === 0) {
      this.validationMessage = 'Please select at least one user for this operation.';
      return;
    }

    this.isSubmitting = true;

    const payload = {
      ...this.formData,
      introductoryTrainingDate: this.formData.introductoryTrainingDate || null,
      workplaceTrainingDate: this.formData.workplaceTrainingDate || null,
      selectedDepartmentId: this.formData.selectedDepartmentId ?? null
    };

    this.http.post<any>(`${environment.apiUrl}/User/bulk-initial-training`, payload)
      .subscribe({
        next: (response) => {
          this.isSubmitting = false;
          this.submitted = true;
          this.submittedCount = response?.updatedCount ?? response?.count ?? 0;
          this.success.emit();
        },
        error: (err) => {
          this.isSubmitting = false;
          console.error('Error applying initial training in bulk:', err);
          this.errorMessage = err?.error?.errors?.[0] || err?.error?.message || 'Error applying initial training in bulk. Please try again.';
        }
      });
  }
}
