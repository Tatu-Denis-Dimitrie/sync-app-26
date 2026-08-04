import { AfterViewInit, Component, ElementRef, NgZone, ViewChild } from '@angular/core';
import { BrowserAuthError, BrowserAuthErrorCodes, PublicClientApplication } from '@azure/msal-browser';

import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthenticationService, LoginRequest } from '../../services/authentication.service';
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
  imports: [FormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements AfterViewInit {
  @ViewChild('googleButton') googleButton?: ElementRef<HTMLDivElement>;

  email: string = '';
  password: string = '';
  errorMessage: string = '';
  isLoading: boolean = false;
  isMicrosoftEnabled: boolean = !!environment.microsoftClientId;

  private msalInstance: PublicClientApplication | null = null;

  constructor(
    private router: Router,
    private authService: AuthenticationService,
    private ngZone: NgZone
  ) {}

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
        // Matches .microsoft-button's max-width so both provider buttons render identically.
        width: '340'
      });
    } catch {
      // Google sign-in is an optional path alongside password login; a script load
      // failure (offline, ad-blocker) should not block the rest of the login page.
    }
  }

  private async initMsal(): Promise<void> {
    if (!environment.microsoftClientId) {
      return;
    }

    const instance = new PublicClientApplication({
      auth: {
        clientId: environment.microsoftClientId,
        authority: 'https://login.microsoftonline.com/common',
        redirectUri: window.location.origin + MSAL_REDIRECT_PATH
      }
    });
    await instance.initialize();
    this.msalInstance = instance;
  }

  onGoogleCredential(response: google.accounts.id.CredentialResponse): void {
    this.errorMessage = '';
    this.isLoading = true;

    this.authService.googleLogin(response.credential).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/loading']);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Google sign-in failed. Please try again.';
      }
    });
  }

  async onMicrosoftLogin(): Promise<void> {
    if (!this.msalInstance) {
      return;
    }

    this.errorMessage = '';
    this.isLoading = true;

    try {
      const result = await this.msalInstance.loginPopup({ scopes: ['openid', 'profile', 'email'] });
      this.authService.microsoftLogin(result.idToken).subscribe({
        next: () => {
          this.isLoading = false;
          this.router.navigate(['/loading']);
        },
        error: (error) => {
          this.isLoading = false;
          this.errorMessage = error.error?.message || 'Microsoft sign-in failed. Please try again.';
        }
      });
    } catch (error: unknown) {
      this.isLoading = false;
      // Closing the popup is a deliberate user action, not a failure to report. Match on
      // the error code rather than the message text, so a genuine failure whose message
      // happens to contain "cancel" still surfaces instead of being silently swallowed.
      const isUserCancelled =
        error instanceof BrowserAuthError && error.errorCode === BrowserAuthErrorCodes.userCancelled;
      if (!isUserCancelled) {
        this.errorMessage = 'Microsoft sign-in failed. Please try again.';
      }
    }
  }

  onLogin(): void {
    this.errorMessage = '';

    if (!this.email || !this.password) {
      this.errorMessage = 'Please enter email and password';
      return;
    }

    this.isLoading = true;

    const loginRequest: LoginRequest = {
      email: this.email,
      password: this.password
    };

    this.authService.login(loginRequest).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.router.navigate(['/loading']);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Login failed. Please try again.';
      }
    });
  }

  onKeyPress(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      this.onLogin();
    }
  }
}
