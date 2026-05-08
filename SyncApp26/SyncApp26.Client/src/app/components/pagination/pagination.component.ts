import { Component, Input, Output, EventEmitter } from '@angular/core';


@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [],
  template: `
    <div class="flex items-center justify-between border-t border-border px-4 py-3 sm:px-6">
      <div class="flex flex-1 justify-between sm:hidden">
        <button
          (click)="onPageChange(currentPage - 1)"
          [disabled]="currentPage === 1"
          class="relative inline-flex items-center rounded-md border border-border bg-background px-4 py-2 text-sm font-medium text-foreground hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
          Previous
        </button>
        <button
          (click)="onPageChange(currentPage + 1)"
          [disabled]="currentPage === totalPages"
          class="relative ml-3 inline-flex items-center rounded-md border border-border bg-background px-4 py-2 text-sm font-medium text-foreground hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
          Next
        </button>
      </div>
      <div class="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
        <div>
          <p class="text-sm text-muted-foreground">
            Showing
            <span class="font-medium">{{ startItem }}</span>
            to
            <span class="font-medium">{{ endItem }}</span>
            of
            <span class="font-medium">{{ totalItems }}</span>
            results
          </p>
        </div>
        <div>
          <nav class="isolate inline-flex -space-x-px rounded-md shadow-sm" aria-label="Pagination">
            <!-- Previous Button -->
            <button
              (click)="onPageChange(currentPage - 1)"
              [disabled]="currentPage === 1"
              class="relative inline-flex items-center rounded-l-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:z-20"
              >
              <span class="sr-only">Previous</span>
              <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd" />
              </svg>
            </button>
    
            <!-- Page Numbers -->
            @for (page of visiblePages; track page) {
              @if (page !== '...') {
                <button
                  (click)="onPageChange(page)"
                  [class.bg-primary]="page === currentPage"
                  [class.text-primary-foreground]="page === currentPage"
                  [class.text-muted-foreground]="page !== currentPage"
                  [class.hover:bg-accent]="page !== currentPage"
                  class="relative inline-flex items-center px-4 py-2 text-sm font-semibold ring-1 ring-inset ring-border focus:z-20 transition-colors"
                  >
                  {{ page }}
                </button>
              }
              @if (page === '...') {
                <span
                  class="relative inline-flex items-center px-4 py-2 text-sm font-semibold text-muted-foreground ring-1 ring-inset ring-border"
                  >
                  ...
                </span>
              }
            }
    
            <!-- Next Button -->
            <button
              (click)="onPageChange(currentPage + 1)"
              [disabled]="currentPage === totalPages"
              class="relative inline-flex items-center rounded-r-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:z-20"
              >
              <span class="sr-only">Next</span>
              <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd" />
              </svg>
            </button>
          </nav>
        </div>
      </div>
    </div>
    `,
  styles: []
})
export class PaginationComponent {
  @Input() currentPage: number = 1;
  @Input() totalItems: number = 0;
  @Input() pageSize: number = 10;
  @Output() pageChange = new EventEmitter<number>();

  get totalPages(): number {
    return Math.ceil(this.totalItems / this.pageSize);
  }

  get startItem(): number {
    return (this.currentPage - 1) * this.pageSize + 1;
  }

  get endItem(): number {
    return Math.min(this.currentPage * this.pageSize, this.totalItems);
  }

  get visiblePages(): (number | string)[] {
    const pages: (number | string)[] = [];
    const maxVisible = 7;

    if (this.totalPages <= maxVisible) {
      for (let i = 1; i <= this.totalPages; i++) {
        pages.push(i);
      }
    } else {
      if (this.currentPage <= 3) {
        for (let i = 1; i <= 5; i++) {
          pages.push(i);
        }
        pages.push('...');
        pages.push(this.totalPages);
      } else if (this.currentPage >= this.totalPages - 2) {
        pages.push(1);
        pages.push('...');
        for (let i = this.totalPages - 4; i <= this.totalPages; i++) {
          pages.push(i);
        }
      } else {
        pages.push(1);
        pages.push('...');
        for (let i = this.currentPage - 1; i <= this.currentPage + 1; i++) {
          pages.push(i);
        }
        pages.push('...');
        pages.push(this.totalPages);
      }
    }

    return pages;
  }

  onPageChange(page: number | string): void {
    if (typeof page === 'number' && page >= 1 && page <= this.totalPages && page !== this.currentPage) {
      this.pageChange.emit(page);
    }
  }
}
