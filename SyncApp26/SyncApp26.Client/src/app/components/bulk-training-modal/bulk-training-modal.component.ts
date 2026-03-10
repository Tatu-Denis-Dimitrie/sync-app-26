import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface BulkTrainingData {
  trainingDate: string;
  durationHours: number | null;
  occupation: string;
  materialTaught: string;
  instructorName: string;
  verifierName: string;
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
export class BulkTrainingModalComponent {
  @Output() close = new EventEmitter<void>();
  @Output() success = new EventEmitter<void>();

  isVisible = false;
  isSubmitting = false;

  formData: BulkTrainingData = {
    trainingDate: '',
    durationHours: null,
    occupation: '',
    materialTaught: '',
    instructorName: '',
    verifierName: '',
    applyToAllUsers: true,
    selectedUserIds: []
  };

  constructor(private http: HttpClient) {}

  open() {
    this.isVisible = true;
    // Set default date to today
    this.formData.trainingDate = new Date().toISOString().split('T')[0];
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
      occupation: '',
      materialTaught: '',
      instructorName: '',
      verifierName: '',
      applyToAllUsers: true,
      selectedUserIds: []
    };
  }

  submitBulkTraining() {
    if (!this.formData.trainingDate) {
      alert('Please select a training date');
      return;
    }

    if (confirm('Are you sure you want to add this periodic training record for all users?')) {
      this.isSubmitting = true;

      this.http.post(`${environment.apiUrl}/PeriodicTraining/bulk`, this.formData)
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
