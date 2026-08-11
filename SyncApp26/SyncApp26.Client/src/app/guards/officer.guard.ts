import { Injectable } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, RouterStateSnapshot, Router } from '@angular/router';
import { AuthenticationService } from '../services/authentication.service';

@Injectable({
  providedIn: 'root'
})
export class OfficerGuard implements CanActivate {
  constructor(
    private authService: AuthenticationService,
    private router: Router
  ) {}

  canActivate(
    route: ActivatedRouteSnapshot,
    state: RouterStateSnapshot
  ): boolean {
    if (!this.authService.isLoggedIn()) {
      this.router.navigate(['/login']);
      return false;
    }

    // Admin keeps access so they can store a signature for later, even though they can no
    // longer use it to sign anything.
    if (!this.authService.isOfficer() && !this.authService.isAdmin()) {
      this.router.navigate(['/access-restricted']);
      return false;
    }

    return true;
  }
}
