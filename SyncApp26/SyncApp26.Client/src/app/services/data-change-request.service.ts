import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, interval } from 'rxjs';
import { switchMap, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { DataChangeRequest, CreateDataChangeRequestDto, ResolveDataChangeRequestDto } from '../models/data-change-request.model';

@Injectable({
  providedIn: 'root'
})
export class DataChangeRequestService {
  private apiUrl = `${environment.apiUrl}/DataChangeRequest`;
  private pendingCount$ = new BehaviorSubject<number>(0);

  constructor(private http: HttpClient) {}

  getAllRequests(): Observable<DataChangeRequest[]> {
    return this.http.get<DataChangeRequest[]>(this.apiUrl);
  }

  getPendingCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.apiUrl}/pending-count`);
  }

  getPendingCount$(): Observable<number> {
    return this.pendingCount$.asObservable();
  }

  loadPendingCount(): void {
    this.getPendingCount().subscribe({
      next: data => this.pendingCount$.next(data.count),
      error: () => {}
    });
  }

  startPollingPendingCount(intervalMs: number = 30000): void {
    interval(intervalMs)
      .pipe(
        switchMap(() => this.getPendingCount()),
        catchError(() => new Observable<{ count: number }>())
      )
      .subscribe(data => this.pendingCount$.next(data.count));
  }

  getMyRequests(): Observable<DataChangeRequest[]> {
    return this.http.get<DataChangeRequest[]>(`${this.apiUrl}/my-requests`);
  }

  createRequest(dto: CreateDataChangeRequestDto): Observable<DataChangeRequest> {
    return this.http.post<DataChangeRequest>(this.apiUrl, dto);
  }

  confirmEmailChange(reqId: string, token: string): Observable<{message: string}> {
    return this.http.get<{message: string}>(`${this.apiUrl}/confirm-email?reqId=${reqId}&token=${token}`);
  }

  resolveRequest(id: string, dto: ResolveDataChangeRequestDto): Observable<{ request: DataChangeRequest; emailError: string | null }> {
    return this.http.put<{ request: DataChangeRequest; emailError: string | null }>(`${this.apiUrl}/${id}/resolve`, dto);
  }
}
