import { UserRole } from '../../models/csv-sync.model';

// Tailwind badge classes for a user role: purple for line managers, blue otherwise.
export function getRoleBadgeColor(role: UserRole | undefined): string {
  return role === UserRole.LineManager
    ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
    : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
}
