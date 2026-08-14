import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { WorkSite } from '../models/csv-sync.model';

@Injectable({ providedIn: 'root' })
export class WorkSiteService {
  private apiUrl = `${environment.apiUrl}/WorkSite`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<WorkSite[]> {
    return this.http.get<WorkSite[]>(this.apiUrl);
  }

  getDeleted(): Observable<WorkSite[]> {
    return this.http.get<WorkSite[]>(`${this.apiUrl}/scheduled-for-deletion`);
  }

  add(name: string, isActive: boolean = true): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(this.apiUrl, { name, isActive });
  }

  update(id: string, name: string, isActive: boolean): Observable<{ success: boolean; message: string }> {
    return this.http.put<{ success: boolean; message: string }>(`${this.apiUrl}/${id}`, { name, isActive });
  }

  delete(id: string): Observable<{ success: boolean; message: string }> {
    return this.http.delete<{ success: boolean; message: string }>(`${this.apiUrl}/${id}`);
  }

  restore(id: string): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.apiUrl}/${id}/restore`, {});
  }
}
