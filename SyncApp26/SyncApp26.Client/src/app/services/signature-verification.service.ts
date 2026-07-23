import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type SignatureVerificationStatusValue = 'Valid' | 'Invalid' | 'ChainBroken' | 'Legacy' | 'NotFound';

export interface SignatureVerificationStatus {
  signatureId: string;
  signerUserId: string;
  status: SignatureVerificationStatusValue;
  isHashValid: boolean;
  isChainValid: boolean;
  isLegacy: boolean;
  verifiedAt: string;
}

@Injectable({ providedIn: 'root' })
export class SignatureVerificationService {
  private base = `${environment.apiUrl}/signatures`;

  constructor(private http: HttpClient) {}

  getVerificationStatus(signatureId: string): Observable<SignatureVerificationStatus> {
    return this.http.get<SignatureVerificationStatus>(`${this.base}/${signatureId}/verification-status`);
  }

  getVerificationStatusBatch(signatureIds: string[]): Observable<SignatureVerificationStatus[]> {
    return this.http.post<SignatureVerificationStatus[]>(`${this.base}/verification-status/batch`, { signatureIds });
  }
}
