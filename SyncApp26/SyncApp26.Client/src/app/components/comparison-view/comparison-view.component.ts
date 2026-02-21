import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UserComparison, FieldConflict } from '../../models/csv-sync.model';

@Component({
  selector: 'app-comparison-view',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
  sortField: 'createdAt' | 'updatedAt' = 'updatedAt';
  sortDirection: 'asc' | 'desc' = 'desc';

  get allUsersSelected(): boolean {
    const filtered = this.getFilteredComparisons();
    return filtered.length > 0 && filtered.every(c => c.selected);
  }

  get allConflictsSelected(): boolean {
    const conflicts = this.getFilteredComparisons().flatMap(c => c.conflicts);
    return conflicts.length > 0 && conflicts.every(conflict => conflict.selected);
  }

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

  toggleSelectAllUsers(): void {
    if (this.allUsersSelected) {
      this.deselectAll();
      return;
    }

    this.selectAll();
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

  toggleSelectAllConflicts(): void {
    const shouldSelectAll = !this.allConflictsSelected;

    this.getFilteredComparisons().forEach(comparison => {
      comparison.conflicts.forEach(conflict => {
        conflict.selected = shouldSelectAll;
        if (shouldSelectAll) {
          conflict.selectedValue = 'csv';
        }
      });
    });
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
    let filtered = this.comparisons.filter(comparison => {
      const statusMatch = 
        (this.filterNew && comparison.status === 'new') ||
        (this.filterModified && comparison.status === 'modified') ||
        (this.filterDeleted && comparison.status === 'deleted');
      
      if (!statusMatch) return false;

      if (this.searchQuery.trim()) {
        const query = this.searchQuery.toLowerCase();
        
        const csvUser = comparison.csvUser;
        const dbUser = comparison.dbUser;
        
        const csvFirstName = csvUser?.firstName?.toLowerCase() || '';
        const csvLastName = csvUser?.lastName?.toLowerCase() || '';
        const csvEmail = csvUser?.email?.toLowerCase() || '';
        const csvDepartment = csvUser?.departmentName?.toLowerCase() || '';
        const csvAssignedTo = csvUser?.assignedToName?.toLowerCase() || '';
        const csvFullName = `${csvFirstName} ${csvLastName}`;
        
        const dbFirstName = dbUser?.firstName?.toLowerCase() || '';
        const dbLastName = dbUser?.lastName?.toLowerCase() || '';
        const dbEmail = dbUser?.email?.toLowerCase() || '';
        const dbDepartment = dbUser?.departmentName?.toLowerCase() || '';
        const dbAssignedTo = dbUser?.assignedToName?.toLowerCase() || '';
        const dbFullName = `${dbFirstName} ${dbLastName}`;
        
        // Check if query matches any field from CSV or DB
        const matchesSearch = csvFullName.includes(query) || 
               dbFullName.includes(query) ||
               csvEmail.includes(query) || 
               dbEmail.includes(query) ||
               csvDepartment.includes(query) || 
               dbDepartment.includes(query) ||
               csvAssignedTo.includes(query) || 
               dbAssignedTo.includes(query);
        
        if (!matchesSearch) return false;
      }

      return true;
    });

    // Sort the filtered comparisons
    filtered = filtered.sort((a, b) => {
      // Use dbUser for date sorting since it has the database timestamps
      const aDate = a.dbUser?.[this.sortField];
      const bDate = b.dbUser?.[this.sortField];
      
      const aValue = aDate ? new Date(aDate).getTime() : 0;
      const bValue = bDate ? new Date(bDate).getTime() : 0;
      
      return this.sortDirection === 'asc' ? aValue - bValue : bValue - aValue;
    });

    return filtered;
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

  toggleSort(field: 'createdAt' | 'updatedAt'): void {
    if (this.sortField === field) {
      // Toggle direction if same field
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      // Set new field with default descending direction
      this.sortField = field;
      this.sortDirection = 'desc';
    }
  }

  getSortIcon(field: 'createdAt' | 'updatedAt'): string {
    if (this.sortField !== field) {
      return '↕️'; // Not sorted
    }
    return this.sortDirection === 'asc' ? '↑' : '↓';
  }

}
