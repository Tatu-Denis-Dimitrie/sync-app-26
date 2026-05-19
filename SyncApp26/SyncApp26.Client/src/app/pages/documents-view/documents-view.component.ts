import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, combineLatest, Observable, of } from 'rxjs';
import { map, catchError, shareReplay } from 'rxjs/operators';
import { AuthenticationService } from '../../services/authentication.service';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { BulkTrainingModalComponent } from '../../components/bulk-training-modal/bulk-training-modal.component';
import { BulkInitialTrainingModalComponent } from '../../components/bulk-initial-training-modal/bulk-initial-training-modal.component';
import { environment } from '../../../environments/environment';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

interface DocumentDto {
  id: string;
  userId: string;
  userFirstName: string;
  userLastName: string;
  userEmail: string;
  userDepartment: string;
  userFunction: string;
  documentType: string;
  status: string;
  generatedAt: string;
  pdfFilePath: string;
  userSignedAt: string | null;
  managerSignedAt: string | null;
  adminSignedAt: string | null;
}

@Component({
  selector: 'app-documents-view',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent, BulkTrainingModalComponent, BulkInitialTrainingModalComponent],
  templateUrl: './documents-view.component.html',
  styleUrl: './documents-view.component.css'
})
export class DocumentsViewComponent implements OnInit {
  @ViewChild(BulkTrainingModalComponent) bulkTrainingModal!: BulkTrainingModalComponent;
  @ViewChild(BulkInitialTrainingModalComponent) bulkInitialTrainingModal!: BulkInitialTrainingModalComponent;
  documents$!: Observable<DocumentDto[]>;
  paginatedDocuments$!: Observable<DocumentDto[]>;

  private allDocuments: DocumentDto[] = [];
  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 15;
  totalItems = 0;
  totalItems$!: Observable<number>;
  filteredDocuments$!: Observable<DocumentDto[]>;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  searchQuery$ = new BehaviorSubject<string>('');
  selectedType$ = new BehaviorSubject<string>('SSM');
  selectedStatus$ = new BehaviorSubject<string>('all');
  selectedSignatureFilter$ = new BehaviorSubject<string>('all');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedType(): string { return this.selectedType$.value; }
  set selectedType(value: string) { this.selectedType$.next(value); }

  get selectedStatus(): string { return this.selectedStatus$.value; }
  set selectedStatus(value: string) { this.selectedStatus$.next(value); }

  get selectedSignatureFilter(): string { return this.selectedSignatureFilter$.value; }
  set selectedSignatureFilter(value: string) { this.selectedSignatureFilter$.next(value); }

  loading = true;
  error: string | null = null;

  // Bulk generate modal state (copied from employees-detail)
  showBulkGenerateModal = false;
  bulkGenerateType: 'SSM' | 'SU' | 'Both' = 'Both';
  isBulkGenerating = false;
  bulkGenerateResult: { message: string; generated: number; skipped: number; adminSignLink?: string | null } | null = null;

  // PDF viewer
  pdfUrl: SafeResourceUrl | null = null;
  showPdfModal = false;
  pdfDocumentName = '';
  successMessage = '';

  // Bulk admin sign
  pendingAdminCount = 0;

  // Regenerate Documents modal state
  showRegenerateModal = false;
  isRegenerating = false;
  regenerateResult: { message: string; regenerated: number } | null = null;


  constructor(
    private http: HttpClient,
    private router: Router,
    private authService: AuthenticationService,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit(): void {
    this.loadDocuments();
    this.loadPendingAdminCount();
  }

  loadPendingAdminCount(): void {
    this.http.get<{ count: number }>(`${environment.apiUrl}/DocumentSignature/pending-ssm-admin-count`)
      .subscribe({
        next: (res) => this.pendingAdminCount = res.count || 0,
        error: () => this.pendingAdminCount = 0
      });
  }

  loadDocuments(): void {
    this.loading = true;
    this.documents$ = this.http.get<DocumentDto[]>(`${environment.apiUrl}/Document/all`).pipe(
      map(docs => {
        this.allDocuments = docs;
        this.loading = false;
        return docs;
      }),
      catchError(err => {
        this.error = 'Failed to load documents.';
        this.loading = false;
        return of([]);
      }),
      shareReplay(1)
    );

    // Subscribe to trigger the HTTP call independently of the template
    this.documents$.subscribe();

    // Derived filtered documents (no side-effects)
    this.filteredDocuments$ = combineLatest([
      this.documents$,
      this.searchQuery$,
      this.selectedType$,
      this.selectedStatus$,
      this.selectedSignatureFilter$
    ]).pipe(
      map(([docs, search, type, status, sigFilter]) => docs.filter(d => {
        const fullName = `${d.userFirstName} ${d.userLastName}`.toLowerCase();
        const matchesSearch = !search ||
          fullName.includes(search.toLowerCase()) ||
          (d.userEmail && d.userEmail.toLowerCase().includes(search.toLowerCase())) ||
          (d.userDepartment && d.userDepartment.toLowerCase().includes(search.toLowerCase()));
        const matchesType = type === 'all' || d.documentType === type;
        const matchesStatus = status === 'all' || d.status === status;
        let matchesSignature = true;
        if (sigFilter === 'employeeSigned') {
          matchesSignature = !!d.userSignedAt && !d.managerSignedAt;
        } else if (sigFilter === 'managerSigned') {
          matchesSignature = !!d.userSignedAt && !!d.managerSignedAt && !d.adminSignedAt;
        } else if (sigFilter === 'completed') {
          matchesSignature = d.status === 'Completed';
        }
        return matchesSearch && matchesType && matchesStatus && matchesSignature;
      })),
      shareReplay(1)
    );

    // total items derived from filteredDocuments$
    this.totalItems$ = this.filteredDocuments$.pipe(
      map(list => list.length),
      shareReplay(1)
    );

    // Paginated view derived from filteredDocuments$
    this.paginatedDocuments$ = combineLatest([
      this.filteredDocuments$,
      this.currentPage$
    ]).pipe(
      map(([filtered, page]) => {
        const start = (page - 1) * this.pageSize;
        return filtered.slice(start, start + this.pageSize);
      })
    );
  }

  // ── Bulk Generate handlers (copied from employees-detail)
  openBulkGenerateModal(): void {
    this.showBulkGenerateModal = true;
    this.bulkGenerateType = 'Both';
    this.bulkGenerateResult = null;
  }

  closeBulkGenerateModal(): void {
    this.showBulkGenerateModal = false;
    this.bulkGenerateResult = null;
  }

  confirmBulkGenerate(): void {
    this.isBulkGenerating = true;
    this.bulkGenerateResult = null;
    this.http.post<any>(`${environment.apiUrl}/Document/bulk-generate`, {
      documentType: this.bulkGenerateType
    }).subscribe({
      next: (res) => {
        this.isBulkGenerating = false;
        this.bulkGenerateResult = res;
        // Reload the documents list to reflect newly generated docs
        this.loadDocuments();
      },
      error: (err) => {
        this.isBulkGenerating = false;
        this.bulkGenerateResult = {
          message: err.error?.message || 'Bulk generation failed.',
          generated: 0,
          skipped: 0
        };
      }
    });
  }

  openBulkTrainingModal(): void {
    this.bulkTrainingModal.open();
  }

  openBulkInitialTrainingModal(): void {
    this.bulkInitialTrainingModal.open();
  }

  onBulkTrainingSuccess(): void {
    this.successMessage = 'Bulk periodic training created successfully for all users!';
    this.loadDocuments();
    setTimeout(() => this.successMessage = '', 5000);
  }

  onBulkInitialTrainingSuccess(): void {
    this.successMessage = 'Bulk initial training applied successfully. Existing initial values were kept unchanged.';
    this.loadDocuments();
    setTimeout(() => this.successMessage = '', 5000);
  }

  onPageChange(page: number): void { this.currentPage = page; }
  onSearchChange(): void { this.currentPage = 1; }
  onFilterChange(): void { this.currentPage = 1; }

  getStatusLabel(status: string): string {
    switch (status) {
      case 'PendingUser': return 'Pending User';
      case 'PendingManager': return 'Pending Manager';
      case 'PendingAdmin': return 'Pending Admin';
      case 'Completed': return 'Completed';
      case 'Superseded': return 'Superseded';
      default: return status;
    }
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'PendingUser': return 'bg-yellow-100 text-yellow-800';
      case 'PendingManager': return 'bg-blue-100 text-blue-800';
      case 'PendingAdmin': return 'bg-orange-100 text-orange-800';
      case 'Completed': return 'bg-green-100 text-green-800';
      case 'Superseded': return 'bg-gray-200 text-gray-500';
      default: return 'bg-gray-100 text-gray-800';
    }
  }

  signAsAdmin(doc: DocumentDto): void {
    this.http.get<{ token: string }>(`${environment.apiUrl}/document/token-for-document/${doc.id}`)
      .subscribe({
        next: (res) => {
          this.router.navigate(['/sign', res.token]);
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to get signing token.';
        }
      });
  }

  bulkSignAsAdmin(): void {
    // Get a token for any pending admin document, then navigate to the signing page in bulk mode
    const pendingAdminDoc = this.allDocuments.find(d => d.status === 'PendingAdmin');
    if (!pendingAdminDoc) {
      this.error = 'No documents pending admin signature.';
      return;
    }
    this.http.get<{ token: string }>(`${environment.apiUrl}/document/token-for-document/${pendingAdminDoc.id}`)
      .subscribe({
        next: (res) => {
          this.router.navigate(['/sign', res.token], { queryParams: { bulk: 'true' } });
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to get signing token.';
        }
      });
  }

  getTypeClass(type: string): string {
    return type === 'SSM' ? 'bg-blue-50 text-blue-700 border-blue-200' : 'bg-red-50 text-red-700 border-red-200';
  }

  viewPdf(doc: DocumentDto): void {
    const url = `${environment.apiUrl}/Document/${doc.id}/view-pdf`;
    this.http.get(url, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        const objectUrl = URL.createObjectURL(blob);
        this.pdfUrl = this.sanitizer.bypassSecurityTrustResourceUrl(objectUrl);
        this.pdfDocumentName = `${doc.userFirstName} ${doc.userLastName} - ${doc.documentType}`;
        this.showPdfModal = true;
      },
      error: () => {
        this.error = 'Failed to open PDF.';
      }
    });
  }

  closePdfModal(): void {
    this.showPdfModal = false;
    this.pdfUrl = null;
  }

  viewEmployee(userId: string): void {
    this.router.navigate(['/employees', userId]);
  }

  openRegenerateModal(): void {
    this.showRegenerateModal = true;
    this.regenerateResult = null;
  }

  closeRegenerateModal(): void {
    this.showRegenerateModal = false;
    this.regenerateResult = null;
  }

  confirmRegenerate(): void {
    this.isRegenerating = true;
    this.regenerateResult = null;
    this.http.post<{ message: string; regenerated: number }>(
      `${environment.apiUrl}/Document/regenerate-documents`, {}
    ).subscribe({
      next: (res) => {
        this.isRegenerating = false;
        this.regenerateResult = res;
      },
      error: (err) => {
        this.isRegenerating = false;
        this.regenerateResult = {
          message: err.error?.message || 'Regenerarea a eșuat.',
          regenerated: 0
        };
      }
    });
  }

  // Navigation
  navigateToDashboard(): void { 
    this.router.navigate(['/dashboard']); 
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
  logout(): void { 
    this.authService.logout(); 
  }
}
