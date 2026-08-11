import { UserRole } from '../../models/csv-sync.model';
import { Roles } from '../../services/authentication.service';

// Tailwind badge classes for a user role: purple for line managers, blue otherwise.
export function getRoleBadgeColor(role: UserRole | undefined): string {
  return role === UserRole.LineManager
    ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
    : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
}

// Tailwind badge classes per role-system role name (chips in the "Roles" list) — a distinct hue per
// built-in role so a user's role set is scannable at a glance; unknown/custom roles fall back to gray.
const ROLE_NAME_BADGE_COLORS: Record<string, string> = {
  [Roles.Admin]: 'bg-indigo-500/10 text-indigo-700 border-indigo-500/20',
  [Roles.LineManager]: 'bg-purple-500/10 text-purple-700 border-purple-500/20',
  [Roles.BasicUser]: 'bg-blue-500/10 text-blue-700 border-blue-500/20',
  [Roles.SsmOfficer]: 'bg-amber-500/10 text-amber-700 border-amber-500/20',
  [Roles.SuOfficer]: 'bg-red-500/10 text-red-700 border-red-500/20'
};

export function roleNameBadgeColor(roleName: string): string {
  return ROLE_NAME_BADGE_COLORS[roleName] ?? 'bg-secondary text-secondary-foreground border-border';
}
