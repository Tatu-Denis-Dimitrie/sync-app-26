import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { catchError, finalize } from 'rxjs/operators';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';

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

  signatureMethod: 'draw' | 'type' = 'draw';
  typedSignature: string = '';
  drawnSignatureData: string = '';

  @ViewChild('signatureCanvas') canvasRef?: ElementRef<HTMLCanvasElement>;
  private isDrawing = false;
  private ctx: CanvasRenderingContext2D | null = null;
  private lastX = 0;
  private lastY = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
  ) { }

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token');
    this.isBulkMode = this.route.snapshot.queryParamMap.get('bulk') === 'true';
    if (!this.token) {
      this.errorMessage = 'Invalid link. No token provided.';
      this.isValidating = false;
      this.isLoading = false;
      return;
    }

    this.validateToken();
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
          setTimeout(() => { if (this.signatureMethod === 'draw') this.initCanvas(); }, 100);
        }
      });
  }

  setSignatureMethod(method: 'draw' | 'type') {
    this.signatureMethod = method;
    if (method === 'draw') {
      setTimeout(() => this.initCanvas(), 100);
    }
  }

  initCanvas(): void {
    if (this.canvasRef && this.canvasRef.nativeElement) {
      const canvas = this.canvasRef.nativeElement;
      this.ctx = canvas.getContext('2d');
      if (this.ctx) {
        this.ctx.lineWidth = 2;
        this.ctx.lineCap = 'round';
        this.ctx.strokeStyle = '#0f766e'; // teal color matching UI
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
    e.preventDefault(); // prevent scrolling while drawing
    const { x, y } = this.getCoordinates(e);

    this.ctx.beginPath();
    this.ctx.moveTo(this.lastX, this.lastY);
    this.ctx.lineTo(x, y);
    this.ctx.stroke();

    this.lastX = x;
    this.lastY = y;

    if (!this.signatureConfirmed) {
      this.signatureConfirmed = true;
    }
  }

  stopDrawing(): void {
    this.isDrawing = false;
  }

  clearSignature(): void {
    if (!this.ctx || !this.canvasRef) return;
    const canvas = this.canvasRef.nativeElement;
    this.ctx.clearRect(0, 0, canvas.width, canvas.height);
    this.signatureConfirmed = false;
  }

  private getCoordinates(e: MouseEvent | TouchEvent): { x: number; y: number } {
    const canvas = this.canvasRef!.nativeElement;
    const rect = canvas.getBoundingClientRect();
    if (window.TouchEvent && e instanceof TouchEvent) {
      return {
        x: e.touches[0].clientX - rect.left,
        y: e.touches[0].clientY - rect.top
      };
    } else {
      const mouseEvent = e as MouseEvent;
      return {
        x: mouseEvent.clientX - rect.left,
        y: mouseEvent.clientY - rect.top
      };
    }
  }

  signDocument(): void {
    if (!this.signatureConfirmed) {
      this.errorMessage = 'You must confirm your signature first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    const payload = {
      token: this.token,
      signatureMethod: this.signatureMethod === 'draw' ? 'Draw' : 'Type',
      signatureData: this.signatureMethod === 'draw' ? this.canvasRef?.nativeElement.toDataURL('image/png') : this.typedSignature,
      bulkSign: this.isBulkMode
    };

    if (this.signatureMethod === 'type' && !this.typedSignature.trim()) {
      this.errorMessage = 'Please type your signature before confirming.';
      this.isLoading = false;
      return;
    }

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
          this.successMessage = res.message || 'Document successfully signed!';
          this.documentData = null; // Hide the form
        }
      });
  }

  goToRegister(): void {
    this.router.navigate(['/register']);
  }
}
