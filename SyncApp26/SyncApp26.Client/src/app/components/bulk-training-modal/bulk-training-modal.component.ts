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

interface BulkTrainingData {
  trainingDate: string;
  durationHours: number | null;
  materialTaught: string;
  instructorName: string;
  verifierName: string;
  documentType: string;
  selectedDepartmentId: string | null;
  applyToAllUsers: boolean;
  selectedUserIds: string[];
}

@Component({
  selector: 'app-bulk-training-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bulk-training-modal.component.html',
  styleUrls: ['./bulk-training-modal.component.css']
})
export class BulkTrainingModalComponent implements OnInit {
  @Output() close = new EventEmitter<void>();
  @Output() success = new EventEmitter<void>();

  isVisible = false;
  isSubmitting = false;
  departments: DepartmentOption[] = [];
  isLoadingDepartments = false;
  users: UserOption[] = [];
  isLoadingUsers = false;
  isUserPickerVisible = false;
  userSearchQuery = '';

  formData: BulkTrainingData = {
    trainingDate: '',
    durationHours: null,
    materialTaught: '',
    instructorName: '',
    verifierName: '',
    documentType: 'Both',
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
      const matchesDepartment = !this.formData.selectedDepartmentId || user.departmentId === this.formData.selectedDepartmentId;
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
    if (!this.formData.applyToAllUsers) {
      const allowedIds = new Set(this.filteredUsers.map((u) => u.id));
      this.formData.selectedUserIds = this.formData.selectedUserIds.filter((id) => allowedIds.has(id));
    }
  }

  openUserPicker(): void {
    this.isUserPickerVisible = true;
    this.userSearchQuery = '';

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
    // Set default date to today
    this.formData.trainingDate = new Date().toISOString().split('T')[0];

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
      durationHours: null,
      materialTaught: '',
      instructorName: '',
      verifierName: '',
      documentType: 'Both',
      selectedDepartmentId: null,
      applyToAllUsers: true,
      selectedUserIds: []
    };
    this.userSearchQuery = '';
  }

  submitBulkTraining() {
    if (!this.formData.trainingDate) {
      alert('Please select a training date');
      return;
    }

    if (!this.formData.applyToAllUsers && this.formData.selectedUserIds.length === 0) {
      alert('Please select at least one user for this training.');
      return;
    }

    const targetText = this.formData.applyToAllUsers
      ? (this.formData.selectedDepartmentId ? 'all users in the selected department' : 'all users')
      : `${this.formData.selectedUserIds.length} selected user(s)`;

    if (confirm(`Are you sure you want to add this periodic training record for ${targetText}?`)) {
      this.isSubmitting = true;

      const payload = {
        ...this.formData,
        selectedDepartmentId: this.formData.selectedDepartmentId ?? null
      };

      this.http.post(`${environment.apiUrl}/PeriodicTraining/bulk`, payload)
        .subscribe({
          next: (response: any) => {
            this.isSubmitting = false;
            alert(`Successfully added training records for ${response.successCount} users`);
            this.success.emit();
            this.closeModal();
          },
          error: (err) => {
            this.isSubmitting = false;
            console.error('Error creating bulk training:', err);
            alert('Error creating bulk training records. Please try again.');
          }
        });
    }
  }
}
