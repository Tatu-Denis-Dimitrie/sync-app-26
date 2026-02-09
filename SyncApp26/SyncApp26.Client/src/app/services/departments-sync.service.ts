import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of } from 'rxjs';
import { map, tap, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { CSVDepartmentComparisonDTO } from '../models/csv-department-sync.model';
import { DepartmentSyncRequestDTO } from '../models/csv-department-sync.model';
import { SyncResult } from '../models/csv-sync.model';

@Injectable({
  providedIn: 'root'
})
export class DepartmentsSyncService {
  private apiUrl = environment.apiUrl + '/CsvSync';
  private currentComparisonSubject = new BehaviorSubject<CSVDepartmentComparisonDTO[] | null>(null);

  currentComparison$ = this.currentComparisonSubject.asObservable();

  constructor(private http: HttpClient) { }

  uploadAndCompareDepartments(file: File): Observable<CSVDepartmentComparisonDTO[]> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<CSVDepartmentComparisonDTO[]>(
      `${this.apiUrl}/upload-departments`,
      formData
    ).pipe(
      tap((comparisons) => {
        this.currentComparisonSubject.next(comparisons);
      }),
      catchError(error => {
        console.error('Error uploading CSV:', error);
        this.currentComparisonSubject.next(null);
        throw error;
      })
    );
  }

  syncDepartments(comparisons: CSVDepartmentComparisonDTO[]): Observable<SyncResult> {
    const syncRequest: DepartmentSyncRequestDTO = {
      items: comparisons
    };

    return this.http.post<SyncResult>(
      `${this.apiUrl}/sync-departments`,
      syncRequest
    ).pipe(
      tap((result) => {
        if (result.success) {
          this.currentComparisonSubject.next(null); //clear comparison on success
        }
      }),
      catchError(error => {
        console.error('Error syncing departments:', error);
        return of({
          success: false,
          recordsProcessed: 0,
          recordsFailed: 0,
          recordsSkipped: 0,
          message: error.error?.error || 'Sync failed',
          errors: [error.message]
        });
      })
    );
  }

  clearComparison(): void {
    this.currentComparisonSubject.next(null);
  }

  getCurrentComparison(): CSVDepartmentComparisonDTO[] | null {
    return this.currentComparisonSubject.value;
  }
}
