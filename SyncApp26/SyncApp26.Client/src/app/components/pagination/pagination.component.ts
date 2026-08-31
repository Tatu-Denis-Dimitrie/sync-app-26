import { Component, Input, Output, EventEmitter, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';


@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagination-root border-t border-border px-4 py-3">
      <div class="pagination-bar">
        <div class="pagination-meta">
          <p class="whitespace-nowrap text-sm text-muted-foreground">
            {{ 'pagination.showing' | translate:'Common' }}
            <span class="font-medium">{{ startItem }}</span>
            {{ 'pagination.to' | translate:'Common' }}
            <span class="font-medium">{{ endItem }}</span>
            {{ 'pagination.of' | translate:'Common' }}
            <span class="font-medium">{{ totalItems }}</span>
            {{ 'pagination.results' | translate:'Common' }}
          </p>
          <div class="flex items-center gap-2">
            <label for="pageSizeSelect" class="whitespace-nowrap text-sm text-muted-foreground">{{ 'pagination.perPage' | translate:'Common' }}</label>
            <select
              id="pageSizeSelect"
              class="rounded-md border border-border bg-background px-2 py-1 text-sm text-foreground hover:bg-accent focus:outline-none focus:ring-1 focus:ring-primary transition-colors"
              (change)="onPageSizeSelect($event)"
              >
              @for (size of pageSizeOptions; track size) {
                <option [value]="size" [selected]="size === pageSize">{{ size }}</option>
              }
              <option value="custom" [selected]="isCustomPageSize">{{ 'pagination.custom' | translate:'Common' }}</option>
            </select>
            @if (showCustomInput) {
              <input
                type="number"
                min="1"
                [ngModel]="customValue ?? pageSize"
                (ngModelChange)="customValue = $event"
                (keyup.enter)="applyCustomPageSize()"
                (keyup.escape)="cancelCustomPageSize()"
                (blur)="applyCustomPageSize()"
                class="w-16 rounded-md border border-border bg-background px-2 py-1 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-primary transition-colors"
                placeholder="#"
                />
            }
          </div>
        </div>
        <div class="pagination-nav-wrap max-w-full overflow-x-auto">
        <nav class="pagination-nav isolate -space-x-px rounded-md shadow-sm" [attr.aria-label]="'pagination.label' | translate:'Common'">
          <!-- Previous Button -->
          <button
            (click)="onPageChange(currentPage - 1)"
            [disabled]="currentPage === 1"
            class="relative inline-flex flex-1 items-center justify-center rounded-l-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:z-20"
            >
            <span class="sr-only">{{ 'pagination.previous' | translate:'Common' }}</span>
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
                class="relative inline-flex flex-1 items-center justify-center px-4 py-2 text-sm font-semibold ring-1 ring-inset ring-border focus:z-20 transition-colors"
                >
                {{ page }}
              </button>
            }
            @if (page === '...') {
              <span
                class="relative inline-flex flex-1 items-center justify-center px-4 py-2 text-sm font-semibold text-muted-foreground ring-1 ring-inset ring-border"
                >
                ...
              </span>
            }
          }

          <!-- Next Button -->
          <button
            (click)="onPageChange(currentPage + 1)"
            [disabled]="currentPage === totalPages"
            class="relative inline-flex flex-1 items-center justify-center rounded-r-md px-2 py-2 text-muted-foreground ring-1 ring-inset ring-border hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:z-20"
            >
            <span class="sr-only">{{ 'pagination.next' | translate:'Common' }}</span>
            <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd" />
            </svg>
          </button>
        </nav>
        </div>
      </div>
    </div>
    `,
  styles: [`
    /* Tailwind's sm: breakpoint reacts to the browser viewport, not this component's own box —
       this component often sits in a narrow column (e.g. a master/detail list) inside a wide
       viewport. A container query keys off the actual rendered width instead, so the bar centers
       and stacks evenly when tight, and lines up left/right like the rest of the app once there's
       room for a single row. */
    .pagination-root {
      container-type: inline-size;
    }
    .pagination-bar {
      display: flex;
      flex-direction: column;
      align-items: stretch;
      gap: 0.75rem;
    }
    .pagination-meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      order: 2;
    }
    .pagination-nav-wrap {
      display: flex;
      order: 1;
    }
    .pagination-nav {
      display: flex;
      width: 100%;
    }
    @container (min-width: 640px) {
      .pagination-bar {
        flex-direction: row;
        align-items: center;
        justify-content: space-between;
      }
      .pagination-meta {
        order: 1;
      }
      .pagination-nav-wrap {
        order: 2;
      }
      .pagination-nav {
        display: inline-flex;
        width: auto;
      }
    }
  `]
})
export class PaginationComponent implements OnInit {
  @Input() currentPage: number = 1;
  @Input() totalItems: number = 0;
  @Input() pageSize: number = 10;
  @Input() pageSizeOptions: number[] = [5, 10, 15];
  // When set, the chosen page size is remembered in sessionStorage under this key and
  // restored on the next visit within the same browser session — see storageKeyPrefix below.
  @Input() storageKey?: string;
  @Output() pageChange = new EventEmitter<number>();
  @Output() pageSizeChange = new EventEmitter<number>();

  private readonly storageKeyPrefix = 'app-pagination:pageSize:';

  customMode = false;
  customValue: number | null = null;

  ngOnInit(): void {
    const saved = this.readStoredPageSize();
    // Deferred so the parent's resulting pageSize update lands in its own change detection
    // cycle, not this component's initial one (avoids ExpressionChangedAfterItHasBeenCheckedError).
    if (saved !== null && saved !== this.pageSize) {
      setTimeout(() => this.emitPageSize(saved));
    }
  }

  private readStoredPageSize(): number | null {
    if (!this.storageKey) return null;
    try {
      const raw = sessionStorage.getItem(this.storageKeyPrefix + this.storageKey);
      const size = raw ? Number(raw) : NaN;
      return size >= 1 ? size : null;
    } catch {
      return null;
    }
  }

  private storePageSize(size: number): void {
    if (!this.storageKey) return;
    try {
      sessionStorage.setItem(this.storageKeyPrefix + this.storageKey, String(size));
    } catch {
      // sessionStorage unavailable (e.g. private browsing) — persistence is best-effort
    }
  }

  private emitPageSize(size: number): void {
    this.storePageSize(size);
    this.pageSizeChange.emit(size);
  }

  get isCustomPageSize(): boolean {
    return !this.pageSizeOptions.includes(this.pageSize);
  }

  get showCustomInput(): boolean {
    return this.customMode || this.isCustomPageSize;
  }

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

  onPageSizeSelect(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (value === 'custom') {
      this.customMode = true;
      return;
    }
    const size = Number(value);
    if (size && size !== this.pageSize) {
      this.emitPageSize(size);
    }
  }

  applyCustomPageSize(): void {
    const size = Math.floor(Number(this.customValue ?? this.pageSize));
    this.customMode = false;
    this.customValue = null;
    if (size >= 1 && size !== this.pageSize) {
      this.emitPageSize(size);
    }
  }

  cancelCustomPageSize(): void {
    this.customMode = false;
    this.customValue = null;
  }
}
