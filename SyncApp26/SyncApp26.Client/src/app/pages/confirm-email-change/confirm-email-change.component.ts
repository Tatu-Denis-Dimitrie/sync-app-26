import { Component, OnInit } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { DataChangeRequestService } from '../../services/data-change-request.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-confirm-email-change',
  standalone: true,
  imports: [TranslatePipe],
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
    private dataChangeService: DataChangeRequestService,
    private translationService: TranslationService
  ) {}

  private tRequests(key: string): string {
    return this.translationService.translate('Requests', key);
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const reqId = params['reqId'];
      const token = params['token'];

      if (!reqId || !token) {
        this.isVerifying = false;
        this.errorMessage = this.tRequests('confirmEmailChange.invalidLinkMissingParams');
        return;
      }

      this.dataChangeService.confirmEmailChange(reqId, token).subscribe({
        next: (res) => {
          this.isVerifying = false;
          this.successMessage = res.message || this.tRequests('confirmEmailChange.emailSuccessfullyVerified');
        },
        error: (err) => {
          this.isVerifying = false;
          this.errorMessage = err.error?.message || this.tRequests('confirmEmailChange.verificationFailedHint');
        }
      });
    });
  }

  goToDashboard(): void {
    this.router.navigate(['/']);
  }
}
