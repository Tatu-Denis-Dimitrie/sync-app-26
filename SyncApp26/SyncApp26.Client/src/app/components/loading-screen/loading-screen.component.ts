import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';

@Component({
  selector: 'app-loading-screen',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './loading-screen.component.html',
  styleUrls: ['./loading-screen.component.css']
})
export class LoadingScreenComponent implements OnInit {
  loadingProgress = 0;
  loadingText = 'Initialization...';
  isCollapsing = false;

  constructor(
    private router: Router,
    private authService: AuthenticationService
  ) {}

  ngOnInit(): void {
    this.simulateLoading();
  }

  private simulateLoading(): void {
    const steps = [
      { progress: 20, text: 'Resources loading...' },
      { progress: 40, text: 'Connecting to server...' },
      { progress: 60, text: 'Synchronizing data...' },
      { progress: 80, text: 'Preparing interface...' },
      { progress: 100, text: 'Done!' }
    ];

    let currentStep = 0;
    const interval = setInterval(() => {
      if (currentStep < steps.length) {
        this.loadingProgress = steps[currentStep].progress;
        this.loadingText = steps[currentStep].text;
        
        // Start collapse animation at 90%
        if (this.loadingProgress >= 90) {
          this.isCollapsing = true;
        }
        
        currentStep++;
      } else {
        clearInterval(interval);
        setTimeout(() => {
          if (this.authService.isAdmin()) {
            this.router.navigate(['/dashboard']);
            return;
          }

          this.router.navigate(['/access-restricted']);
        }, 300);
      }
    }, 500);
  }
}
