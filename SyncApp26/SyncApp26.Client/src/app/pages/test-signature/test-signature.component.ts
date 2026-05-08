import { Component } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-test-signature',
  standalone: true,
  imports: [FormsModule, RouterModule],
  templateUrl: './test-signature.component.html',
  styleUrls: ['./test-signature.component.css']
})
export class TestSignatureComponent {
  email: string = '';
  documentName: string = 'Test Document 2026';

  isLoading: boolean = false;
  successMessage: string = '';
  errorMessage: string = '';

  constructor(private http: HttpClient) { }

  generateLink(): void {
    if (!this.email) {
      this.errorMessage = 'Please enter an email address.';
      return;
    }

    this.isLoading = true;
    this.successMessage = '';
    this.errorMessage = '';

    const payload = {
      email: this.email,
      documentId: '00000000-0000-0000-0000-000000000000', // Dummy GUID for testing
      documentName: this.documentName
    };

    this.http.post<any>(`${environment.apiUrl}${environment.endpoints.documentSignature}/request-signature`, payload)
      .subscribe({
        next: (response) => {
          this.isLoading = false;
          this.successMessage = response.message || 'Signature request sent successfully! Check your email inbox or backend logs.';
          this.email = ''; // Reset
        },
        error: (error) => {
          this.isLoading = false;
          this.errorMessage = error.error?.message || error.message || 'An error occurred while generating the link.';
        }
      });
  }
}
