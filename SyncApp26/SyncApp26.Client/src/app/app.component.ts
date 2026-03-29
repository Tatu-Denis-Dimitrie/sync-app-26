import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { FooterComponent } from './components/footer/footer.component';
import { HeaderComponent } from './components/header/header.component';
import { LoadingService } from './services/loading.service';
import { Observable, Subscription, filter } from 'rxjs';

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, FooterComponent, HeaderComponent],
  standalone: true,
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'SyncApp26.Client';
  loading$!: Observable<boolean>;
  showHeader = true;
  private routerSubscription!: Subscription;

  constructor(
    private loadingService: LoadingService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loading$ = this.loadingService.loading$;
    
    // Initial check
    this.updateHeaderVisibility(this.router.url);

    // Listen to route changes
    this.routerSubscription = this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      this.updateHeaderVisibility(event.urlAfterRedirects || event.url);
    });
    
    // Simulate app initialization
    this.loadingService.finishLoading();
  }

  ngOnDestroy(): void {
    if (this.routerSubscription) {
      this.routerSubscription.unsubscribe();
    }
  }

  private updateHeaderVisibility(url: string): void {
    const publicRoutes = ['/login', '/register', '/forgot-password', '/reset-password', '/reset-password/', '/sign/'];
    // Check if the current URL starts with any of the public routes
    this.showHeader = !publicRoutes.some(route => url.startsWith(route));
  }
}
