import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { DataChangeRequest, CreateDataChangeRequestDto, ResolveDataChangeRequestDto } from '../models/data-change-request.model';

@Injectable({
  providedIn: 'root'
})
export class DataChangeRequestService {
  private apiUrl = `${environment.apiUrl}/DataChangeRequest`;

  constructor(private http: HttpClient) {}

  getAllRequests(): Observable<DataChangeRequest[]> {
    return this.http.get<DataChangeRequest[]>(this.apiUrl);
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

  resolveRequest(id: string, dto: ResolveDataChangeRequestDto): Observable<DataChangeRequest> {
    return this.http.put<DataChangeRequest>(`${this.apiUrl}/${id}/resolve`, dto);
  }
}
