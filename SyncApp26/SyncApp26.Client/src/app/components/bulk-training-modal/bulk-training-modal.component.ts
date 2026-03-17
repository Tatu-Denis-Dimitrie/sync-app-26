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

  open() {
    this.isVisible = true;
    // Set default date to today
    this.formData.trainingDate = new Date().toISOString().split('T')[0];

    // Reload in case departments changed while modal was closed
    if (!this.departments.length) {
      this.loadDepartments();
    }
  }

  closeModal() {
    this.isVisible = false;
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
  }

  submitBulkTraining() {
    if (!this.formData.trainingDate) {
      alert('Please select a training date');
      return;
    }

    const targetText = this.formData.selectedDepartmentId
      ? 'users in the selected department'
      : 'all users';

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
