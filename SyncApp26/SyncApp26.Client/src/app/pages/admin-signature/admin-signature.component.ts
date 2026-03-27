import { Component, ElementRef, ViewChild } from '@angular/core';
import { UserSignatureService } from '../../services/user-signature.service';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-admin-signature',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-signature.component.html',
  styleUrls: ['./admin-signature.component.css']
})
export class AdminSignatureComponent {
  @ViewChild('signaturePad') signaturePad?: ElementRef<HTMLCanvasElement>;
  private ctx?: CanvasRenderingContext2D;
  private drawing = false;
  private lastX = 0;
  private lastY = 0;

  signatureMethod: 'draw' | 'type' = 'draw';
  typedSignature: string = '';
  savedSignature: any = null;
  isSigConfirmed = false;
  currentYear = new Date().getFullYear();

  ngOnInit() {
    this.loadSignature();
  }

  ngAfterViewChecked() {
    this.initCanvas();
  }

  private initCanvas() {
    if (this.signatureMethod === 'draw' && this.signaturePad && this.signaturePad.nativeElement) {
      this.ctx = this.signaturePad.nativeElement.getContext('2d')!;
      this.ctx.strokeStyle = '#0f766e';
      this.ctx.lineWidth = 2.5;
      this.ctx.lineCap = 'round';
      this.ctx.lineJoin = 'round';
    }
  }

  loadSignature() {
    this.userSignatureService.getMySignature().subscribe(sig => {
      this.savedSignature = sig;
    });
  }

  constructor(
    private userSignatureService: UserSignatureService,
    private router: Router,
    private authService: AuthenticationService
  ) {}

  ngAfterViewInit() {
    this.initCanvas();  
  }

  setSignatureMethod(method: 'draw' | 'type') {
    this.signatureMethod = method;
    setTimeout(() => this.initCanvas(), 0);
    this.isSigConfirmed = false;
    if (method === 'draw') {
      setTimeout(() => this.clearSignature(), 100);
    } else {
      this.typedSignature = '';
    }
  }

  onTypedSignatureChange() {
    this.isSigConfirmed = !!this.typedSignature.trim();
  }

  startDrawing(event: MouseEvent | TouchEvent) {
    if (!this.ctx) return;
    this.drawing = true;
    this.isSigConfirmed = true;
    const { x, y } = this.getXY(event);
    this.lastX = x;
    this.lastY = y;
  }

  stopDrawing() {
    this.drawing = false;
  }

  draw(event: MouseEvent | TouchEvent) {
    if (!this.drawing || !this.ctx) return;
    event.preventDefault();
    this.isSigConfirmed = true;
    const { x, y } = this.getXY(event);
    this.ctx.beginPath();
    this.ctx.moveTo(this.lastX, this.lastY);
    this.ctx.lineTo(x, y);
    this.ctx.stroke();
    this.lastX = x;
    this.lastY = y;
  }

  clearSignature() {
    if (this.ctx && this.signaturePad) {
      this.ctx.clearRect(0, 0, this.signaturePad.nativeElement.width, this.signaturePad.nativeElement.height);
    }
  }

  saveSignature() {
    if (!this.isSigConfirmed) return;
    let payload;
    if (this.signatureMethod === 'draw') {
      if (!this.signaturePad) return;
      const dataUrl = this.signaturePad.nativeElement.toDataURL('image/png');
      payload = { signatureData: dataUrl, signatureMethod: 'Draw' };
    } else {
      payload = { signatureData: this.typedSignature, signatureMethod: 'Type' };
    }
    this.userSignatureService.saveMySignature(payload).subscribe({
      next: (response) => {
        console.log('Răspuns salvare semnătură admin:', response);
        alert('Semnătura a fost salvată!');
        this.loadSignature();
        this.isSigConfirmed = false;
        this.clearSignature();
      },
      error: (err) => {
        console.error('Eroare la salvarea semnăturii admin:', err);
        alert('Eroare la salvarea semnăturii!');
      }
    });
  }

  private getXY(event: MouseEvent | TouchEvent): { x: number, y: number } {
    const canvas = this.signaturePad?.nativeElement;
    if (!canvas) return { x: 0, y: 0 };
    let x = 0, y = 0;
    if (event instanceof MouseEvent) {
      const rect = canvas.getBoundingClientRect();
      x = event.offsetX;
      y = event.offsetY;
      // Fallback for browsers that don't support offsetX/Y
      if (typeof x !== 'number' || typeof y !== 'number') {
        x = event.clientX - rect.left;
        y = event.clientY - rect.top;
      }
    } else if (event.touches && event.touches.length > 0) {
      const rect = canvas.getBoundingClientRect();
      x = event.touches[0].clientX - rect.left;
      y = event.touches[0].clientY - rect.top;
    }
    // Normalize for high-DPI screens
    if (canvas.width && canvas.height && canvas.offsetWidth && canvas.offsetHeight) {
      x = x * (canvas.width / canvas.offsetWidth);
      y = y * (canvas.height / canvas.offsetHeight);
    }
    return { x, y };
  }

  navigateToDepartments(): void {
    this.router.navigate(['/departments']);
  }

  navigateToUsers(): void {
    this.router.navigate(['/users']);
  }

  navigateToEmployees(): void {
    this.router.navigate(['/employees']);
  }

  navigateToImportHistory(): void {
    this.router.navigate(['/import-history']);
  }

  navigateToSignature(): void {
    this.router.navigate(['/admin-signature']);
  }

  
  navigateToDataRequests(): void {
    this.router.navigate(['/data-requests']);
  }
navigateToDocuments(): void {
    this.router.navigate(['/documents']);
  }

  navigateToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  logout(): void {
    this.authService.logout();
  }
}
