import { Component } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterModule, TranslatePipe],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.css']
})
export class ForgotPasswordComponent {
  email: string = '';
  errorMessage: string = '';
  successMessage: string = '';
  isLoading: boolean = false;

  constructor(
    private router: Router,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) {}

  private t(key: string): string {
    return this.translationService.translate('Auth', key);
  }

  onSubmit(): void {
    if (this.isLoading) {
      return;
    }

    if (!this.email) {
      this.errorMessage = this.t('forgotPassword.pleaseEnterEmail');
      return;
    }

    // Basic email validation
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(this.email)) {
      this.errorMessage = this.t('forgotPassword.invalidEmail');
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.authService.forgotPassword({ email: this.email.trim() }).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || this.t('forgotPassword.resetLinkSent');
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || this.t('forgotPassword.sendFailed');
      }
    });
  }
}
