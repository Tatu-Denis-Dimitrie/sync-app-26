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

// Which SignatureCanonicalSerializer schema computed this signature's HMAC — not a resign
// counter. isMostRecentSignature is the resign order, derived from signedAt, not version.
export interface SignatureVersionSummary {
  signatureId: string;
  version: number;
  isMostRecentSignature: boolean;
  signerRole: string;
  signerUserId: string;
  signerFullNameSnapshot: string;
  signedAt: string;
  status: SignatureVerificationStatusValue;
}

export interface PeriodicTrainingSignatureHistory {
  periodicTrainingId: string;
  userId: string;
  versionsByRole: Record<string, SignatureVersionSummary[]>;
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

  getSignatureHistory(periodicTrainingId: string): Observable<PeriodicTrainingSignatureHistory> {
    return this.http.get<PeriodicTrainingSignatureHistory>(`${this.base}/training/${periodicTrainingId}/history`);
  }

  // Keyed by userId (as a string, since JSON object keys are always strings).
  getVerificationStatusForUsers(userIds: string[]): Observable<Record<string, SignatureVerificationStatus[]>> {
    return this.http.post<Record<string, SignatureVerificationStatus[]>>(`${this.base}/verification-status/by-users`, { userIds });
  }
}
