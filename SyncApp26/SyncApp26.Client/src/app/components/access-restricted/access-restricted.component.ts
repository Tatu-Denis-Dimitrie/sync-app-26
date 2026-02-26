import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthenticationService, User } from '../../services/authentication.service';

@Component({
  selector: 'app-access-restricted',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './access-restricted.component.html',
  styleUrls: ['./access-restricted.component.css']
})
export class AccessRestrictedComponent implements OnInit {
  currentUser: User | null = null;

  constructor(
    private authService: AuthenticationService,
    private router: Router
  ) {}

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
