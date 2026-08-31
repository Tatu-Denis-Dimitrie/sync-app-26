import { Component, OnInit } from '@angular/core';

import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { TranslationService } from '../../services/translation.service';

@Component({
  selector: 'app-loading-screen',
  standalone: true,
  imports: [],
  templateUrl: './loading-screen.component.html',
  styleUrls: ['./loading-screen.component.css']
})
export class LoadingScreenComponent implements OnInit {
  loadingProgress = 0;
  loadingText = '';
  isCollapsing = false;

  constructor(
    private router: Router,
    private authService: AuthenticationService,
    private translationService: TranslationService
  ) {
    this.loadingText = this.tCommon('loadingScreen.initialization');
  }

  private tCommon(key: string): string {
    return this.translationService.translate('Common', key);
  }

  ngOnInit(): void {
    this.simulateLoading();
  }

  private simulateLoading(): void {
    const steps = [
      { progress: 20, text: this.tCommon('loadingScreen.resourcesLoading') },
      { progress: 40, text: this.tCommon('loadingScreen.connectingToServer') },
      { progress: 60, text: this.tCommon('loadingScreen.synchronizingData') },
      { progress: 80, text: this.tCommon('loadingScreen.preparingInterface') },
      { progress: 100, text: this.tCommon('loadingScreen.done') }
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

          if (this.authService.isLineManager()) {
            this.router.navigate(['/line-manager']);
            return;
          }

          this.router.navigate(['/basic-user']);
        }, 300);
      }
    }, 500);
  }
}
