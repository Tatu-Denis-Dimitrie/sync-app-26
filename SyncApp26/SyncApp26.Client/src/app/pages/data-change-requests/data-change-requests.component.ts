import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { DataChangeRequest } from '../../models/data-change-request.model';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { BloodType, BLOOD_TYPE_LABELS } from '../../models/csv-sync.model';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-data-change-requests',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './data-change-requests.component.html',
  styleUrls: ['./data-change-requests.component.css']
})
export class DataChangeRequestsComponent implements OnInit {
  requests: DataChangeRequest[] = [];
  isLoading = true;
  error = '';
  success = '';
  warning = '';

  constructor(
    private service: DataChangeRequestService,
    private router: Router,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) { }

  private tRequests(key: string): string {
    return this.translationService.translate('Requests', key);
  }

  statusLabel(status: string): string {
    const keys: Record<string, string> = {
      Pending: 'status.pending',
      Approved: 'status.approved',
      Rejected: 'status.rejected'
    };
    return keys[status] ? this.tRequests(keys[status]) : status;
  }

  ngOnInit(): void {
    this.loadRequests();
  }

  loadRequests(): void {
    this.isLoading = true;
    this.service.getAllRequests().subscribe({
      next: (reqs) => {
        this.requests = reqs;
        this.isLoading = false;
      },
      error: (err) => {
        this.error = this.tRequests('dataChangeRequests.failedToLoad');
        this.isLoading = false;
      }
    });
  }

  getParsedChanges(json: string): any {
    try {
      return JSON.parse(json);
    } catch {
      return {};
    }
  }

  getOriginalValues(req: DataChangeRequest): any {
    if (!req.originalValuesJson) return {};
    try {
      return JSON.parse(req.originalValuesJson);
    } catch {
      return {};
    }
  }

  getOriginalValue(req: DataChangeRequest, key: PropertyKey): unknown {
    return this.getOriginalValues(req)[key as string];
  }

  getFieldLabel(key: PropertyKey): string {
    return String(key).replace(/([A-Z])/g, ' $1').trim();
  }

  getDisplayValue(key: PropertyKey, value: unknown): unknown {
    if (key === 'BloodType' && typeof value === 'string' && value in BLOOD_TYPE_LABELS) {
      return BLOOD_TYPE_LABELS[value as BloodType];
    }
    return value;
  }

  resolveRequest(id: string, status: 'Approved' | 'Rejected'): void {
    this.error = '';
    this.success = '';
    this.warning = '';

    if (status === 'Rejected') {
      const confirmReject = confirm(this.tRequests('dataChangeRequests.confirmReject'));
      if (!confirmReject) return;
    }

    this.service.resolveRequest(id, { status }).subscribe({
      next: (res) => {
        const index = this.requests.findIndex(r => r.id === id);
        if (index !== -1) {
          this.requests[index] = res.request;
        }
        if (res.emailError) {
          this.warning = this.tRequests(status === 'Approved'
            ? 'dataChangeRequests.requestApprovedEmailFailed'
            : 'dataChangeRequests.requestRejectedEmailFailed');
        } else {
          this.success = this.tRequests(status === 'Approved'
            ? 'dataChangeRequests.requestApproved'
            : 'dataChangeRequests.requestRejected');
        }
        this.service.loadPendingCount();
      },
      error: (err) => {
        this.error = err.error?.message || this.tRequests(status === 'Approved'
          ? 'dataChangeRequests.failedToApprove'
          : 'dataChangeRequests.failedToReject');
      }
    });
  }

  navigateToDashboard(): void { this.router.navigate(['/dashboard']); }
  navigateToDepartments(): void { this.router.navigate(['/departments']); }
  navigateToImportHistory(): void { this.router.navigate(['/import-history']); }
  navigateToUsers(): void { this.router.navigate(['/users']); }
  navigateToEmployees(): void { this.router.navigate(['/employees']); }
  navigateToDocuments(): void { this.router.navigate(['/documents']); }
  navigateToSignature(): void { this.router.navigate(['/admin-signature']); }
  logout(): void { this.authService.logout(); }
}
