import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { DataChangeRequestService } from '../../services/data-change-request.service';

@Component({
  selector: 'app-confirm-email-change',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './confirm-email-change.component.html',
  styleUrls: ['./confirm-email-change.component.css']
})
export class ConfirmEmailChangeComponent implements OnInit {
  isVerifying = true;
  successMessage = '';
  errorMessage = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private dataChangeService: DataChangeRequestService
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const reqId = params['reqId'];
      const token = params['token'];

      if (!reqId || !token) {
        this.isVerifying = false;
        this.errorMessage = 'Invalid verification link. Missing parameters.';
        return;
      }

      this.dataChangeService.confirmEmailChange(reqId, token).subscribe({
        next: (res) => {
          this.isVerifying = false;
          this.successMessage = res.message || 'Email successfully verified!';
        },
        error: (err) => {
          this.isVerifying = false;
          this.errorMessage = err.error?.message || 'Verification failed. The link may have expired or is invalid.';
        }
      });
    });
  }

  goToDashboard(): void {
    this.router.navigate(['/']);
  }
}
