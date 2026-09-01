import { Component, OnInit } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [FormsModule, RouterModule, TranslatePipe],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.css']
})
export class ResetPasswordComponent implements OnInit {
  email: string = '';
  token: string = '';
  newPassword: string = '';
  confirmPassword: string = '';
  errorMessage: string = '';
  successMessage: string = '';
  isLoading: boolean = false;

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) {}

  private t(key: string): string {
    return this.translationService.translate('Auth', key);
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      this.email = params['email'] || '';
      this.token = params['token'] || '';
    });
  }

  onSubmit(): void {
    if (this.isLoading) {
      return;
    }

    this.errorMessage = '';

    if (!this.email) {
      this.errorMessage = this.t('resetPassword.emailRequired');
      return;
    }

    if (!this.token) {
      this.errorMessage = this.t('resetPassword.invalidToken');
      return;
    }

    // Validate new password
    if (!this.newPassword || this.newPassword.length < 6) {
      this.errorMessage = this.t('resetPassword.passwordMinLength');
      return;
    }

    // Check if passwords match
    if (this.newPassword !== this.confirmPassword) {
      this.errorMessage = this.t('register.passwordsDoNotMatch');
      return;
    }

    this.isLoading = true;

    this.authService.resetPassword({
      email: this.email.trim(),
      token: this.token.trim(),
      newPassword: this.newPassword
    }).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || this.t('resetPassword.passwordResetSuccessfully');
        setTimeout(() => {
          this.router.navigate(['/login']);
        }, 1200);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || this.t('resetPassword.resetFailed');
      }
    });
  }
}
