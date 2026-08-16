import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface SignatureAnomalyAlertRecord {
  id: string;
  recordsChecked: number;
  anomaliesFound: number;
  occurredAt: string;
}

@Injectable({ providedIn: 'root' })
export class SignatureAnomalyAlertService {
  private apiUrl = `${environment.apiUrl}/signature-anomaly-alerts`;

  constructor(private http: HttpClient) {}

  getUnread(): Observable<SignatureAnomalyAlertRecord[]> {
    return this.http.get<SignatureAnomalyAlertRecord[]>(`${this.apiUrl}/unread`);
  }

  dismissAll(): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/dismiss-all`, {});
  }
}
