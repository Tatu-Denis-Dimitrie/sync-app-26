import { Component, Input, Output, EventEmitter } from '@angular/core';
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
export class ComparisonViewComponent {
  @Input() comparisons: UserComparison[] = [];
  @Output() selectionChange = new EventEmitter<UserComparison[]>();
  @Output() fieldConflictResolved = new EventEmitter<{ comparisonId: string, field: string, value: 'db' | 'csv' }>();

  filterNew = true;
  filterModified = true;
  filterDeleted = true;
  searchQuery = '';

  selectAll(): void {
    const filtered = this.getFilteredComparisons();
    filtered.forEach(c => c.selected = true);
    this.selectionChange.emit(this.comparisons);
  }

  deselectAll(): void {
    const filtered = this.getFilteredComparisons();
    filtered.forEach(c => c.selected = false);
    this.selectionChange.emit(this.comparisons);
  }

  toggleSelection(comparison: UserComparison): void {
    comparison.selected = !comparison.selected;
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
    return new Date(date).toLocaleDateString();
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
        
        // Search in both CSV and DB user fields
        const csvUser = comparison.csvUser;
        const dbUser = comparison.dbUser;
        
        const csvFirstName = csvUser?.firstName?.toLowerCase() || '';
        const csvLastName = csvUser?.lastName?.toLowerCase() || '';
        const csvEmail = csvUser?.email?.toLowerCase() || '';
        const csvDepartment = csvUser?.departmentName?.toLowerCase() || '';
        const csvAssignedTo = csvUser?.assignedToName?.toLowerCase() || '';
        
        const dbFirstName = dbUser?.firstName?.toLowerCase() || '';
        const dbLastName = dbUser?.lastName?.toLowerCase() || '';
        const dbEmail = dbUser?.email?.toLowerCase() || '';
        const dbDepartment = dbUser?.departmentName?.toLowerCase() || '';
        const dbAssignedTo = dbUser?.assignedToName?.toLowerCase() || '';
        
        const csvFullName = `${csvFirstName} ${csvLastName}`;
        const dbFullName = `${dbFirstName} ${dbLastName}`;
        
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

}
