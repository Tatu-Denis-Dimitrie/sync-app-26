import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface UserSignature {
  id: string;
  userId: string;
  signatureData: string;    // Base64 image
  signatureMethod: string;  // 'Draw' | 'Type'
  signatureHash: string;
  createdAt: string;
  updatedAt?: string;
  isActive: boolean;
}

export interface UserSignatureHistory {
  id: string;
  userId: string;
  signatureMethod: string;
  signatureHash: string;
  action: string;           // 'Created' | 'Updated' | 'Revoked'
  ipAddress?: string;
  performedByUserId: string;
  performedByEmail: string;
  createdAt: string;
}

export interface SaveSignatureRequest {
  signatureData: string;
  signatureMethod: string;
}

@Injectable({ providedIn: 'root' })
export class UserSignatureService {
  private base = `${environment.apiUrl}/usersignature`;

  constructor(private http: HttpClient) {}

  getMySignature(): Observable<UserSignature> {
    return this.http.get<UserSignature>(`${this.base}/my`);
  }

  saveMySignature(req: SaveSignatureRequest): Observable<{ message: string; signature: UserSignature }> {
    return this.http.post<{ message: string; signature: UserSignature }>(`${this.base}/save`, req);
  }

  revokeMySignature(): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.base}/revoke`);
  }

  getMyHistory(): Observable<UserSignatureHistory[]> {
    return this.http.get<UserSignatureHistory[]>(`${this.base}/my/history`);
  }
}
