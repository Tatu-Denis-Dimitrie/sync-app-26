import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { UserComparison, FieldConflict } from '../../models/csv-sync.model';

@Component({
  selector: 'app-comparison-view',
  standalone: true,
  imports: [CommonModule, FormsModule, ScrollingModule],
  templateUrl: './comparison-view.component.html',
  styleUrls: ['./comparison-view.component.css']
})
export class ComparisonViewComponent implements OnChanges {
  @Input() comparisons: UserComparison[] = [];
  @Output() selectionChange = new EventEmitter<UserComparison[]>();
  @Output() fieldConflictResolved = new EventEmitter<{ comparisonId: string, field: string, value: 'db' | 'csv' }>();

  filterNew = true;
  filterModified = true;
  filterDeleted = true;
  searchQuery = '';
  private explicitlySelected = new Set<string>();
  private explicitlyDeselected = new Set<string>();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['comparisons'] && changes['comparisons'].previousValue !== changes['comparisons'].currentValue) {
      this.filterNew = true;
      this.filterModified = true;
      this.filterDeleted = true;
      this.searchQuery = '';
    }
  }

  selectAll(): void {
    const filteredIds = new Set(this.getFilteredComparisons().map(c => c.id));
    this.comparisons.forEach(c => {
      if (filteredIds.has(c.id)) {
        c.selected = true;
        this.explicitlySelected.add(c.id);
        this.explicitlyDeselected.delete(c.id);
      }
    });
    this.selectionChange.emit(this.comparisons);
  }

  deselectAll(): void {
    const filteredIds = new Set(this.getFilteredComparisons().map(c => c.id));
    this.comparisons.forEach(c => {
      if (filteredIds.has(c.id)) {
        c.selected = false;
        this.explicitlyDeselected.add(c.id);
        this.explicitlySelected.delete(c.id);
      }
    });
    this.selectionChange.emit(this.comparisons);
  }

  toggleStatusFilter(status: 'new' | 'modified' | 'deleted'): void {
    let isEnabled = false;

    if (status === 'new') {
      this.filterNew = !this.filterNew;
      isEnabled = this.filterNew;
    } else if (status === 'modified') {
      this.filterModified = !this.filterModified;
      isEnabled = this.filterModified;
    } else {
      this.filterDeleted = !this.filterDeleted;
      isEnabled = this.filterDeleted;
    }

    this.updateSelectionForStatus(status, isEnabled);
    this.onFilterChange();
  }

  toggleSelection(comparison: UserComparison): void {
    comparison.selected = !comparison.selected;

    if (comparison.selected) {
      this.explicitlySelected.add(comparison.id);
      this.explicitlyDeselected.delete(comparison.id);
    } else {
      this.explicitlyDeselected.add(comparison.id);
      this.explicitlySelected.delete(comparison.id);
    }

    this.selectionChange.emit(this.comparisons);
  }

  resolveConflict(comparisonId: string, field: string, value: 'db' | 'csv'): void {
    const comparison = this.comparisons.find(c => c.id === comparisonId);
    if (comparison) {
      const conflict = comparison.conflicts.find(c => c.field === field);
      if (conflict) {
        conflict.selectedValue = value;
        conflict.selected = true;
        this.fieldConflictResolved.emit({ comparisonId, field, value });
      }
    }
  }

  toggleFieldSelection(comparisonId: string, field: string): void {
    const comparison = this.comparisons.find(c => c.id === comparisonId);
    if (comparison) {
      const conflict = comparison.conflicts.find(c => c.field === field);
      if (conflict) {
        conflict.selected = !conflict.selected;
      }
    }
  }

  isFieldSelected(comparison: UserComparison, field: string): boolean {
    const conflict = comparison.conflicts.find(c => c.field === field);
    return conflict?.selected || false;
  }

  getSelectedFieldsCount(comparison: UserComparison): number {
    return comparison.conflicts.filter(c => c.selected).length;
  }

  getStatusColor(status: string): string {
    switch (status) {
      case 'new':
        return 'bg-blue-500/10 text-blue-700 border-blue-500/20';
      case 'modified':
        return 'bg-yellow-500/10 text-yellow-700 border-yellow-500/20';
      case 'unchanged':
        return 'bg-green-500/10 text-green-700 border-green-500/20';
      case 'deleted':
        return 'bg-red-500/10 text-red-700 border-red-500/20';
      default:
        return 'bg-muted text-muted-foreground border-border';
    }
  }

  getStatusIcon(status: string): string {
    switch (status) {
      case 'new':
        return '➕';
      case 'modified':
        return '✏️';
      case 'unchanged':
        return '✓';
      case 'deleted':
        return '🗑️';
      default:
        return '?';
    }
  }

  hasConflicts(comparison: UserComparison): boolean {
    return comparison.conflicts.length > 0;
  }

  getConflictCount(): number {
    return this.getFilteredComparisons().filter(c => c.conflicts.length > 0).length;
  }

  getSelectedCount(): number {
    return this.getFilteredComparisons().filter(c => c.selected).length;
  }

  formatFieldName(field: string): string {
    return field.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    const d = new Date(date);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = String(d.getFullYear()).slice(-2);
    return `${day}/${month}/${year}`;
  }

  hasFieldConflict(comparison: UserComparison, field: string): boolean {
    return comparison.conflicts.some(c => c.field === field);
  }

  getFilteredComparisons(): UserComparison[] {
    return this.comparisons.filter(comparison => {
      const statusMatch = 
        (this.filterNew && comparison.status === 'new') ||
        (this.filterModified && comparison.status === 'modified') ||
        (this.filterDeleted && comparison.status === 'deleted');
      
      if (!statusMatch) return false;

      if (this.searchQuery.trim()) {
        const query = this.searchQuery.toLowerCase();
        
        const user = comparison.csvUser || comparison.dbUser;
        const firstName = user?.firstName?.toLowerCase() || '';
        const lastName = user?.lastName?.toLowerCase() || '';
        const email = user?.email?.toLowerCase() || '';
        const department = user?.departmentName?.toLowerCase() || '';
        const assignedToName = user?.assignedToName?.toLowerCase() || '';
        const fullName = `${firstName} ${lastName}`;
        
        const directMatch = fullName.includes(query) || department.includes(query) || email.includes(query);

        const assignedToMatch = assignedToName.includes(query);
        
        // Check if query matches any field from CSV or DB
        return csvFullName.includes(query) || 
               dbFullName.includes(query) ||
               csvEmail.includes(query) || 
               dbEmail.includes(query) ||
               csvDepartment.includes(query) || 
               dbDepartment.includes(query) ||
               csvAssignedTo.includes(query) || 
               dbAssignedTo.includes(query);
      }

      return true;
    });
  }

  onFilterChange(): void {
  }

  private updateSelectionForStatus(status: 'new' | 'modified' | 'deleted', isEnabled: boolean): void {
    this.comparisons.forEach(comparison => {
      if (comparison.status === status) {
        if (!isEnabled) {
          if (comparison.selected) {
            this.explicitlySelected.add(comparison.id);
            this.explicitlyDeselected.delete(comparison.id);
          } else {
            this.explicitlyDeselected.add(comparison.id);
            this.explicitlySelected.delete(comparison.id);
          }

          comparison.selected = false;
          return;
        }

        if (this.explicitlySelected.has(comparison.id)) {
          comparison.selected = true;
          return;
        }

        if (this.explicitlyDeselected.has(comparison.id)) {
          comparison.selected = false;
          return;
        }

        comparison.selected = false;
      }
    });

    this.selectionChange.emit(this.comparisons);
  }

}
