import { Component, OnInit, AfterViewInit, ViewChild, ElementRef, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { catchError, finalize } from 'rxjs/operators';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { AuthenticationService } from '../../services/authentication.service';
import { UserSignatureService, UserSignature } from '../../services/user-signature.service';
import { CanvasSignaturePad } from '../../shared/utils/canvas-signature-pad';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

@Component({
  selector: 'app-document-signature',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, TranslatePipe],
  templateUrl: './document-signature.component.html',
  styleUrls: ['./document-signature.component.css']
})
export class DocumentSignatureComponent implements OnInit {
  token: string | null = null;
  isBulkMode = false;
  isLoading = true;
  isValidating = true;
  errorMessage = '';
  documentData: any = null;
  signatureConfirmed = false;
  successMessage = '';

  // Auth state
  isLoggedIn = false;

  // Saved signature
  savedSignature: UserSignature | null = null;
  isSavedSignatureLoaded = false;
  isUsingSavedSignature = false;

  signatureMethod: 'draw' | 'type' | 'saved' = 'draw';
  typedSignature: string = '';

  // Bulk signing progress
  bulkTotal = 0;
  bulkSigned = 0;

  @ViewChild('signatureCanvas') canvasRef?: ElementRef<HTMLCanvasElement>;
  private sigPad = new CanvasSignaturePad();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private authService: AuthenticationService,
    private userSignatureService: UserSignatureService,
    private cdr: ChangeDetectorRef,
    private translationService: TranslationService
  ) { }

  tDocuments(key: string): string {
    return this.translationService.translate('Documents', key);
  }

  ngOnInit(): void {
    this.isLoggedIn = this.authService.isLoggedIn();
    this.token = this.route.snapshot.paramMap.get('token');
    this.isBulkMode = this.route.snapshot.queryParamMap.get('bulk') === 'true';

    if (!this.token) {
      this.errorMessage = this.tDocuments('documentSignature.invalidLinkNoToken');
      this.isValidating = false;
      this.isLoading = false;
      return;
    }

    this.validateToken();

    if (this.isLoggedIn) {
      this.loadSavedSignature();
    }
  }

  loadSavedSignature(): void {
    this.userSignatureService.getMySignature().subscribe({
      next: (sig) => {
        this.savedSignature = sig;
        this.isSavedSignatureLoaded = true;
        // Default to saved signature if available
        if (sig?.isActive) {
          this.setSignatureMethod('saved');
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.isSavedSignatureLoaded = true; // 404 means no saved sig — that's fine
      }
    });
  }

  validateToken(): void {
    this.http.get<any>(`${environment.apiUrl}${environment.endpoints.documentSignature}/validate-token/${this.token}`)
      .pipe(
        finalize(() => {
          this.isValidating = false;
          this.isLoading = false;
        }),
        catchError(error => {
          this.errorMessage = error.error?.message || this.tDocuments('documentSignature.linkInvalidOrExpired');
          return of(null);
        })
      )
      .subscribe(data => {
        if (data) {
          this.documentData = data;
          setTimeout(() => { if (this.signatureMethod === 'draw') this.initCanvas(); }, 100);

          // Bulk: preia numărul total de documente de semnat pentru responsabilul SSM/SU, acum că
          // tipul documentului e cunoscut.
          if (this.isBulkMode && this.isLoggedIn && this.authService.isOfficer()) {
            this.http.get<any>(`${environment.apiUrl}/documentsignature/pending-ssm-admin-count`, {
              params: { documentType: this.documentData.documentType }
            }).subscribe({
              next: (res) => {
                this.bulkTotal = res?.count || 0;
                this.bulkSigned = 0;
              },
              error: () => {
                this.bulkTotal = 0;
              }
            });
          }
        }
      });
  }

  setSignatureMethod(method: 'draw' | 'type' | 'saved') {
    this.signatureMethod = method;
    this.signatureConfirmed = false;
    if (method === 'draw') {
      setTimeout(() => this.initCanvas(), 100);
    } else if (method === 'saved' && this.savedSignature?.isActive) {
      this.signatureConfirmed = true;
    }
  }

  initCanvas(): void {
    this.sigPad.attach(this.canvasRef?.nativeElement);
  }

  startDrawing(e: MouseEvent | TouchEvent): void {
    this.sigPad.startDrawing(e);
  }

  draw(e: MouseEvent | TouchEvent): void {
    if (this.sigPad.draw(e)) this.signatureConfirmed = true;
  }

  stopDrawing(): void {
    this.sigPad.stopDrawing();
  }

  clearCanvas(): void {
    this.sigPad.clear();
    this.signatureConfirmed = false;
  }

  getSignaturePayload(): { method: string; data: string } {
    if (this.signatureMethod === 'saved' && this.savedSignature) {
      return { method: this.savedSignature.signatureMethod, data: this.savedSignature.signatureData };
    }
    if (this.signatureMethod === 'type') {
      return { method: 'Type', data: this.typedSignature };
    }
    return { method: 'Draw', data: this.canvasRef?.nativeElement.toDataURL('image/png') ?? '' };
  }

  signDocument(): void {
    if (this.signatureMethod === 'type' && !this.typedSignature.trim()) {
      this.errorMessage = this.tDocuments('documentSignature.pleaseTypeFullName');
      return;
    }
    if (this.signatureMethod === 'saved') {
      if (!this.savedSignature || !this.savedSignature.isActive) {
        this.errorMessage = this.tDocuments('documentSignature.noActiveSavedSignature');
        return;
      }
    }
    if (!this.signatureConfirmed) {
      this.errorMessage = this.tDocuments('documentSignature.mustConfirmBeforeSigning');
      return;
    }

    const { method, data } = this.getSignaturePayload();
    this.isLoading = true;
    this.errorMessage = '';

    // Bulk sign cu progres real (responsabil SSM/SU)
    if (this.isBulkMode && this.bulkTotal > 0 && this.authService.isOfficer()) {
      this.bulkSigned = 0;
      this.successMessage = '';
      const payload = {
        signatureMethod: method,
        signatureData: data,
        documentType: this.documentData?.documentType
      };
      // DEBUG: log payload trimis la bulk-sign-async
      console.log('Bulk sign payload:', payload);
      this.http.post<any>(`${environment.apiUrl}${environment.endpoints.documentSignature}/bulk-sign-async`, payload)
        .subscribe(res => {
          if (res && res.jobId) {
            this.bulkTotal = res.total;
            this.bulkSigned = 0;
            this.pollBulkProgress(res.jobId);
          } else {
            this.isLoading = false;
            this.errorMessage = res?.message || this.tDocuments('api.noDocumentsToSign');
          }
        }, err => {
          this.isLoading = false;
          this.errorMessage = err.error?.message || this.tDocuments('documentSignature.failedToStartBulk');
        });
      return;
    }

    // Semnare normală sau bulk fără progres real
    const payload = {
      token: this.token,
      signatureMethod: method,
      signatureData: data,
      bulkSign: this.isBulkMode,
      periodicTrainingId: this.documentData?.periodicTrainingId ?? null
    };
    // DEBUG: log payload trimis la consume-token
    console.log('Sign payload:', payload);
    this.http.post<any>(`${environment.apiUrl}${environment.endpoints.documentSignature}/consume-token`, payload)
      .pipe(
        finalize(() => this.isLoading = false),
        catchError(error => {
          this.errorMessage = error.error?.message || this.tDocuments('documentSignature.failedToSign');
          return of(null);
        })
      )
      .subscribe(res => {
        if (res) {
          this.documentData = null;
          if (this.isBulkMode && typeof res.count === 'number' && res.count > 1) {
            this.bulkTotal = res.count;
            this.bulkSigned = res.count;
            this.successMessage = this.translationService.translate('Documents', 'api.successfullySignedCount', res.count);
          } else {
            this.successMessage = res.message || this.tDocuments('documentSignature.documentSignedSuccess');
          }
        }
      });
  }

  pollBulkProgress(jobId: string): void {
    const poll = () => {
      this.http.get<any>(`${environment.apiUrl}${environment.endpoints.documentSignature}/bulk-sign-status/${jobId}`)
        .subscribe(res => {
          if (res) {
            this.bulkTotal = res.total;
            this.bulkSigned = res.signed;
            if (res.completed) {
              this.isLoading = false;
              if (res.error) {
                this.errorMessage = this.translationService.translate('Documents', 'documentSignature.bulkSigningError', res.error);
              } else {
                this.successMessage = this.translationService.translate('Documents', 'api.successfullySignedCount', res.signed);
                this.documentData = null;
              }
            } else {
              setTimeout(poll, 700);
            }
          } else {
            this.isLoading = false;
            this.errorMessage = this.tDocuments('documentSignature.bulkStatusError');
          }
        }, err => {
          this.isLoading = false;
          this.errorMessage = this.tDocuments('documentSignature.bulkStatusError');
        });
    };
    poll();
  }

  goToDashboard(): void {
    const user = this.authService.getCurrentUser();
    if (!user) { this.router.navigate(['/login']); return; }
    if (this.authService.isAdmin()) this.router.navigate(['/documents']);
    else if (this.authService.isLineManager()) this.router.navigate(['/line-manager']);
    else this.router.navigate(['/basic-user']);
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }
}
