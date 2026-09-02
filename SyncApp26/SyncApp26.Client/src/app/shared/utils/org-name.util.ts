// Department and function names are free-form data (seeded, CSV-synced, admin-edited), but the
// built-in set that ships with the app has proper translations - same idea as roleLabel() for the
// fixed role names. Anything not in these maps (e.g. "IT", "Marketing") is shown verbatim.

export type OrgNameKind = 'department' | 'function';

// Keyed by the exact English name as stored. Values are keys in the Common translation scope.
const DEPARTMENT_LABEL_KEYS: Record<string, string> = {
  'Engineering': 'department.engineering',
  'Human Resources': 'department.humanResources',
  'Sales': 'department.sales',
  'Finance': 'department.finance'
};

const FUNCTION_LABEL_KEYS: Record<string, string> = {
  'Team Lead': 'function.teamLead',
  'Software Engineer': 'function.softwareEngineer',
  'QA Engineer': 'function.qaEngineer',
  'HR Director': 'function.hrDirector',
  'HR Specialist': 'function.hrSpecialist',
  'Recruiter': 'function.recruiter',
  'Sales Director': 'function.salesDirector',
  'Account Executive': 'function.accountExecutive',
  'Sales Representative': 'function.salesRepresentative',
  'Marketing Director': 'function.marketingDirector',
  'Content Specialist': 'function.contentSpecialist',
  'Digital Marketing Specialist': 'function.digitalMarketingSpecialist',
  'Financial Analyst': 'function.financialAnalyst',
  'Accountant': 'function.accountant'
};

/**
 * Returns the localized label for a department/function name, falling back to the name itself
 * (trimmed) when there is no known translation.
 */
export function orgNameLabel(
  kind: OrgNameKind,
  name: string | null | undefined,
  translate: (key: string) => string
): string {
  const raw = (name ?? '').trim();
  if (!raw) {
    return raw;
  }

  const key = (kind === 'department' ? DEPARTMENT_LABEL_KEYS : FUNCTION_LABEL_KEYS)[raw];
  return key ? translate(key) : raw;
}
