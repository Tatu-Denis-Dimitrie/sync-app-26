import { Component, OnInit } from '@angular/core';
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
  isLoading = true;
  isValidating = true;
  errorMessage = '';
  documentData: any = null;
  signatureConfirmed = false;
  successMessage = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
  ) { }

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token');
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
        }
      });
  }

  signDocument(): void {
    if (!this.signatureConfirmed) {
      this.errorMessage = 'You must confirm your signature first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    const payload = {
      token: this.token
      // Additional form answers would go here later
    };

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
