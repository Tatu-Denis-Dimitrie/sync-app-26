import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, interval } from 'rxjs';
import { environment } from '../../environments/environment';
import { switchMap, catchError } from 'rxjs/operators';
import { UserSyncSignalrService } from './user-sync.signalr.service';

@Injectable({ providedIn: 'root' })
export class DocumentSignatureService {
  private base = `${environment.apiUrl}/documentsignature`;
  private pendingDocumentsCount$ = new BehaviorSubject<number>(0);

  constructor(
    private http: HttpClient,
    private signalrService: UserSyncSignalrService
  ) {
    // Listen to signature updates from SignalR
    this.signalrService.signatureUpdated$.subscribe(() => {
      this.loadPendingDocumentsCount();
    });
  }

  getPendingSsmAdminCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.base}/pending-ssm-admin-count`);
  }

  getPendingDocumentsCount$(): Observable<number> {
    return this.pendingDocumentsCount$.asObservable();
  }

  setPendingDocumentsCount(count: number): void {
    this.pendingDocumentsCount$.next(count);
  }

  startPollingPendingDocuments(intervalMs: number = 30000): void {
    interval(intervalMs)
      .pipe(
        switchMap(() => this.getPendingSsmAdminCount()),
        catchError(() => {
          // Silently handle errors to prevent breaking the polling
          return new Observable<{ count: number }>();
        })
      )
      .subscribe(data => {
        this.setPendingDocumentsCount(data.count);
      });
  }

  // Initial load
  loadPendingDocumentsCount(): void {
    this.getPendingSsmAdminCount().subscribe(
      data => this.setPendingDocumentsCount(data.count),
      error => console.error('Failed to load pending documents count', error)
    );
  }
}
