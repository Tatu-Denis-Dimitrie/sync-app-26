import { AfterViewInit, Component, ElementRef, NgZone, ViewChild } from '@angular/core';
import { BrowserAuthError, BrowserAuthErrorCodes, PublicClientApplication } from '@azure/msal-browser';

import { switchMap } from 'rxjs';

import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthenticationService, LoginRequest } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { environment } from '../../../environments/environment';
import { MSAL_REDIRECT_PATH } from '../../auth/msal-redirect-path';

const GOOGLE_SCRIPT_ID = 'google-gsi';

function loadGoogleScript(): Promise<void> {
  if (document.getElementById(GOOGLE_SCRIPT_ID)) {
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.id = GOOGLE_SCRIPT_ID;
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Failed to load Google Identity Services script.'));
    document.head.appendChild(script);
  });
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements AfterViewInit {
  @ViewChild('googleButton') googleButton?: ElementRef<HTMLDivElement>;

  email: string = '';
  password: string = '';
  errorMessage: string = '';
  isLoading: boolean = false;
  showPassword: boolean = false;
  isMicrosoftEnabled: boolean = !!environment.microsoftClientId;

  private msalInstance: PublicClientApplication | null = null;

  constructor(
    private router: Router,
    private authService: AuthenticationService,
    private translationService: TranslationService,
    private ngZone: NgZone
  ) {}

  /** Shorthand for the Auth scope - every message on this page comes from it. */
  private t(key: string): string {
    return this.translationService.translate('Auth', key);
  }

  async ngAfterViewInit(): Promise<void> {
    await Promise.all([
      this.initGoogleButton(),
      this.initMsal()
    ]);
  }

  private async initGoogleButton(): Promise<void> {
    if (!environment.googleClientId || !this.googleButton) {
      return;
    }

    try {
      await loadGoogleScript();
      window.google?.accounts.id.initialize({
        client_id: environment.googleClientId,
        callback: (response) => this.ngZone.run(() => this.onGoogleCredential(response))
      });
      window.google?.accounts.id.renderButton(this.googleButton.nativeElement, {
        type: 'standard',
        theme: 'outline',
        size: 'large',
        text: 'signin_with',
        shape: 'rectangular',
        // Matches .microsoft-button so both provider buttons are the same size.
        width: '340'
      });
    } catch {
      // Optional path - a script load failure must not break the login page.
    }
  }

  private async initMsal(): Promise<void> {
    if (!environment.microsoftClientId) {
      return;
    }

    try {
      const instance = new PublicClientApplication({
        auth: {
          clientId: environment.microsoftClientId,
          authority: 'https://login.microsoftonline.com/common',
          redirectUri: window.location.origin + MSAL_REDIRECT_PATH
        }
      });
      await instance.initialize();
      this.msalInstance = instance;
    } catch {
      // Swallowed so it can't break the rest of the page; onMicrosoftLogin reports it.
    }
  }

  onGoogleCredential(response: google.accounts.id.CredentialResponse): void {
    this.errorMessage = '';
    this.isLoading = true;

    this.authService.googleLogin(response.credential).pipe(
      switchMap(() => this.translationService.applyPreferredLanguage())
    ).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/loading']);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || this.t('login.googleSignInFailed');
      }
    });
  }

  async onMicrosoftLogin(): Promise<void> {
    if (!this.msalInstance) {
      this.errorMessage = this.t('login.microsoftUnavailable');
      return;
    }

    this.errorMessage = '';
    this.isLoading = true;

    try {
      const result = await this.msalInstance.loginPopup({ scopes: ['openid', 'profile', 'email'] });
      this.authService.microsoftLogin(result.idToken).pipe(
        switchMap(() => this.translationService.applyPreferredLanguage())
      ).subscribe({
        next: () => {
          this.isLoading = false;
          this.router.navigate(['/loading']);
        },
        error: (error) => {
          this.isLoading = false;
          this.errorMessage = error.error?.message || this.t('login.microsoftSignInFailed');
        }
      });
    } catch (error: unknown) {
      this.isLoading = false;
      // Closing the popup isn't a failure. Match on the code, not the message text.
      const isUserCancelled =
        error instanceof BrowserAuthError && error.errorCode === BrowserAuthErrorCodes.userCancelled;
      if (!isUserCancelled) {
        this.errorMessage = this.t('login.microsoftSignInFailed');
      }
    }
  }

  onLogin(): void {
    if (this.isLoading) {
      return;
    }

    this.errorMessage = '';

    if (!this.email || !this.password) {
      this.errorMessage = this.t('login.pleaseEnterCredentials');
      return;
    }

    this.isLoading = true;

    const loginRequest: LoginRequest = {
      email: this.email,
      password: this.password
    };

    this.authService.login(loginRequest).pipe(
      switchMap(() => this.translationService.applyPreferredLanguage())
    ).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/loading']);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || this.t('login.loginFailed');
      }
    });
  }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }
}
