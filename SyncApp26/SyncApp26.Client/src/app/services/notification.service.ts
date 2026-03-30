import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface NotificationRequest {
  documentType: 'SSM' | 'SU';
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private apiUrl = `${environment.apiUrl}/Notification`;

  constructor(private http: HttpClient) { }

  notifyUser(userId: string, documentType: 'SSM' | 'SU'): Observable<any> {
    const payload: NotificationRequest = { documentType };
    return this.http.post(`${this.apiUrl}/notify-user/${userId}`, payload);
  }

  notifyManager(managerId: string, documentType: 'SSM' | 'SU'): Observable<any> {
    const payload: NotificationRequest = { documentType };
    return this.http.post(`${this.apiUrl}/notify-manager/${managerId}`, payload);
  }

  notifyAllManagers(documentType: 'SSM' | 'SU'): Observable<any> {
    const payload: NotificationRequest = { documentType };
    return this.http.post(`${this.apiUrl}/notify-all-managers`, payload);
  }
}
