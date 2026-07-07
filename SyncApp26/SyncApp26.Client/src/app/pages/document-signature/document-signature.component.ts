import { Component, OnInit, AfterViewInit, ViewChild, ElementRef, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { catchError, finalize } from 'rxjs/operators';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { AuthenticationService, AuthRole } from '../../services/authentication.service';
import { UserSignatureService, UserSignature } from '../../services/user-signature.service';

@Component({
  selector: 'app-document-signature',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
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
  private isDrawing = false;
  private ctx: CanvasRenderingContext2D | null = null;
  private lastX = 0;
  private lastY = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private authService: AuthenticationService,
    private userSignatureService: UserSignatureService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    this.isLoggedIn = this.authService.isLoggedIn();
    this.token = this.route.snapshot.paramMap.get('token');
    this.isBulkMode = this.route.snapshot.queryParamMap.get('bulk') === 'true';

    // Bulk: preia numărul total de documente de semnat pentru admin
    if (this.isBulkMode && this.isLoggedIn && this.authService.getCurrentUser()?.role === AuthRole.Admin) {
      this.http.get<any>(`${environment.apiUrl}/documentsignature/pending-ssm-admin-count`).subscribe({
        next: (res) => {
          this.bulkTotal = res?.count || 0;
          this.bulkSigned = 0;
        },
        error: () => {
          this.bulkTotal = 0;
        }
      });
    }

    if (!this.token) {
      this.errorMessage = 'Invalid link. No token provided.';
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
          this.errorMessage = error.error?.message || 'The secure link is invalid or has expired.';
          return of(null);
        })
      )
      .subscribe(data => {
        if (data) {
          this.documentData = data;
          // Adaugă flag pentru semnare ca admin (verificator SSM)
          const user = this.authService.getCurrentUser();
          this.documentData.isAdminSigning = !!(user && user.role === AuthRole.Admin && this.documentData.documentType === 'SSM');
          setTimeout(() => { if (this.signatureMethod === 'draw') this.initCanvas(); }, 100);
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
    if (this.canvasRef && this.canvasRef.nativeElement) {
      const canvas = this.canvasRef.nativeElement;
      this.ctx = canvas.getContext('2d');
      if (this.ctx) {
        this.ctx.lineWidth = 2.5;
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';
        this.ctx.strokeStyle = '#0f766e';
      }
    }
  }

  startDrawing(e: MouseEvent | TouchEvent): void {
    if (!this.ctx || !this.canvasRef) return;
    this.isDrawing = true;
    const { x, y } = this.getCoordinates(e);
    this.lastX = x;
    this.lastY = y;
  }

  draw(e: MouseEvent | TouchEvent): void {
    if (!this.isDrawing || !this.ctx) return;
    e.preventDefault();
    const { x, y } = this.getCoordinates(e);

    this.ctx.beginPath();
    this.ctx.moveTo(this.lastX, this.lastY);
    this.ctx.lineTo(x, y);
    this.ctx.stroke();

    this.lastX = x;
    this.lastY = y;
    this.signatureConfirmed = true;
  }

  stopDrawing(): void {
    this.isDrawing = false;
  }

  clearCanvas(): void {
    if (!this.ctx || !this.canvasRef) return;
    const canvas = this.canvasRef.nativeElement;
    this.ctx.clearRect(0, 0, canvas.width, canvas.height);
    this.signatureConfirmed = false;
  }

  private getCoordinates(e: MouseEvent | TouchEvent): { x: number; y: number } {
    const canvas = this.canvasRef!.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    if (window.TouchEvent && e instanceof TouchEvent) {
      return {
        x: (e.touches[0].clientX - rect.left) * scaleX,
        y: (e.touches[0].clientY - rect.top) * scaleY
      };
    }
    const m = e as MouseEvent;
    return {
      x: (m.clientX - rect.left) * scaleX,
      y: (m.clientY - rect.top) * scaleY
    };
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
      this.errorMessage = 'Please type your full name as your signature.';
      return;
    }
    if (this.signatureMethod === 'saved') {
      if (!this.savedSignature || !this.savedSignature.isActive) {
        this.errorMessage = 'No active saved signature found.';
        return;
      }
    }
    if (!this.signatureConfirmed) {
      this.errorMessage = 'You must confirm your signature before signing.';
      return;
    }

    const { method, data } = this.getSignaturePayload();
    this.isLoading = true;
    this.errorMessage = '';

    // Bulk sign cu progres real (admin)
    if (this.isBulkMode && this.bulkTotal > 0 && this.authService.getCurrentUser()?.role === AuthRole.Admin) {
      this.bulkSigned = 0;
      this.successMessage = '';
      const payload = {
        signatureMethod: method,
        signatureData: data
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
            this.errorMessage = res?.message || 'No documents to sign.';
          }
        }, err => {
          this.isLoading = false;
          this.errorMessage = err.error?.message || 'Failed to start bulk signing.';
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
          this.errorMessage = error.error?.message || 'Failed to sign the document. Please try again.';
          return of(null);
        })
      )
      .subscribe(res => {
        if (res) {
          this.documentData = null;
          if (this.isBulkMode && typeof res.count === 'number' && res.count > 1) {
            this.bulkTotal = res.count;
            this.bulkSigned = res.count;
            this.successMessage = `Successfully signed ${res.count} document(s).`;
          } else {
            this.successMessage = res.message || 'Document successfully signed!';
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
                this.errorMessage = 'Bulk signing error: ' + res.error;
              } else {
                this.successMessage = `Successfully signed ${res.signed} document(s).`;
                this.documentData = null;
              }
            } else {
              setTimeout(poll, 700);
            }
          } else {
            this.isLoading = false;
            this.errorMessage = 'Bulk signing status error.';
          }
        }, err => {
          this.isLoading = false;
          this.errorMessage = 'Bulk signing status error.';
        });
    };
    poll();
  }

  goToDashboard(): void {
    const user = this.authService.getCurrentUser();
    if (!user) { this.router.navigate(['/login']); return; }
    if (user.role === AuthRole.Admin) this.router.navigate(['/documents']);
    else if (user.role === AuthRole.LineManager) this.router.navigate(['/line-manager']);
    else this.router.navigate(['/basic-user']);
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }
}
