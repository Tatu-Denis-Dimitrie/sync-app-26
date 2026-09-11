import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { LanguageSwitcherComponent } from '../../components/language-switcher/language-switcher.component';
import { AuthenticationService } from '../../services/authentication.service';

// Public page - required by Google/Microsoft to publish the OAuth app.
@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  imports: [RouterModule, TranslatePipe, LanguageSwitcherComponent],
  templateUrl: './privacy-policy.component.html',
  styleUrls: ['./privacy-policy.component.css']
})
export class PrivacyPolicyComponent {
  constructor(private authService: AuthenticationService) {}

  // The app's own header already shows (with its own language switcher) once logged in -
  // this page's topbar would just duplicate it.
  get isLoggedIn(): boolean {
    return this.authService.isLoggedIn();
  }

  readonly sections = [
    'data',
    'purpose',
    'access',
    'signin',
    'cookies',
    'retention',
    'rights',
    'contact'
  ];
}
