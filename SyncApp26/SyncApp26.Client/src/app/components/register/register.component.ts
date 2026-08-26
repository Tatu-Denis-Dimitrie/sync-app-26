import { Component } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthenticationService, RegisterRequest } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { isValidName, NAME_ERROR_MESSAGE } from '../../shared/utils/name-validation.util';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.css']
})
export class RegisterComponent {
  firstName: string = '';
  lastName: string = '';
  email: string = '';
  password: string = '';
  confirmPassword: string = '';
  errorMessage: string = '';
  successMessage: string = '';
  isLoading: boolean = false;

  constructor(
    private router: Router,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) {}

  /** Shorthand for the Auth scope - every message on this page comes from it. */
  private t(key: string): string {
    return this.translationService.translate('Auth', key);
  }

  onRegister(): void {
    if (this.isLoading) {
      return;
    }

    this.errorMessage = '';
    this.successMessage = '';

    if (!this.firstName || !this.lastName || !this.email || !this.password || !this.confirmPassword) {
      this.errorMessage = this.t('register.pleaseFillAllFields');
      return;
    }

    if (!isValidName(this.firstName) || !isValidName(this.lastName)) {
      // NAME_ERROR_MESSAGE stays untranslated for now - it's shared across several forms outside
      // the Auth scope (see shared/utils/name-validation.util.ts), not something this page owns.
      this.errorMessage = `First/last name: ${NAME_ERROR_MESSAGE}`;
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage = this.t('register.passwordsDoNotMatch');
      return;
    }

    this.isLoading = true;

    const registerRequest: RegisterRequest = {
      firstName: this.firstName,
      lastName: this.lastName,
      email: this.email,
      password: this.password
    };

    this.authService.register(registerRequest).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || this.t('register.registrationSuccessful');

        // Redirect to login after 2 seconds
        setTimeout(() => {
          this.router.navigate(['/login']);
        }, 2000);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || this.t('register.registrationFailed');
      }
    });
  }
}
