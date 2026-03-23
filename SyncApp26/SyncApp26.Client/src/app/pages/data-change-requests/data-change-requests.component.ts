import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { DataChangeRequest } from '../../models/data-change-request.model';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';

@Component({
  selector: 'app-data-change-requests',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './data-change-requests.component.html',
  styleUrls: ['./data-change-requests.component.css']
})
export class DataChangeRequestsComponent implements OnInit {
  requests: DataChangeRequest[] = [];
  isLoading = true;
  error = '';
  success = '';

  constructor(
    private service: DataChangeRequestService,
    private router: Router,
    private authService: AuthenticationService
  ) { }

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
        this.error = 'Failed to load requests.';
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

  resolveRequest(id: string, status: 'Approved' | 'Rejected'): void {
    this.error = '';
    this.success = '';
    
    if (status === 'Rejected') {
      const confirmReject = confirm('Are you sure you want to reject this request?');
      if (!confirmReject) return;
    }

    this.service.resolveRequest(id, { status }).subscribe({
      next: (res) => {
        this.success = `Request has been ${status.toLowerCase()}.`;
        const index = this.requests.findIndex(r => r.id === id);
        if (index !== -1) {
          this.requests[index] = res;
        }
      },
      error: (err) => {
        this.error = err.error?.message || `Failed to ${status.toLowerCase()} request.`;
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
