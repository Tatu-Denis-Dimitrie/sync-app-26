import { Component, OnInit } from '@angular/core';

import { Router } from '@angular/router';
import { AuthenticationService, User, rolesLabel } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-access-restricted',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './access-restricted.component.html',
  styleUrls: ['./access-restricted.component.css']
})
export class AccessRestrictedComponent implements OnInit {
  rolesLabel = rolesLabel;
  currentUser: User | null = null;

  constructor(
    private authService: AuthenticationService,
    private router: Router,
    private translationService: TranslationService
  ) {}

  tCommon(key: string): string {
    return this.translationService.translate('Common', key);
  }

  ngOnInit(): void {
    this.currentUser = this.authService.getCurrentUser();
    
    // If not logged in, redirect to login
    if (!this.currentUser) {
      this.router.navigate(['/login']);
    }
  }

  onLogout(): void {
    this.authService.logout();
  }
}
