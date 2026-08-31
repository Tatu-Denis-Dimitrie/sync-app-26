// Shared limits/validators for person-name and job-title/function free-text fields,
// kept in sync with SyncApp26.Shared/Validation/NameValidationConstants.cs on the backend.

export const NAME_MAX_LENGTH = 100;
export const FUNCTION_MAX_LENGTH = 100;

// Unicode letters, optionally separated by single spaces/hyphens/apostrophes; must
// start and end with a letter (or be a single letter). Covers diacritics (e.g. Romanian
// ă â î ș ț) and compound/hyphenated names (Anne-Marie, O'Brien).
export const NAME_PATTERN = /^\p{L}$|^\p{L}[\p{L}\s'-]*\p{L}$/u;

export function isValidName(value: string | null | undefined): boolean {
  if (!value) return false;
  return value.length <= NAME_MAX_LENGTH && NAME_PATTERN.test(value);
}

export function isValidFunction(value: string | null | undefined): boolean {
  if (!value) return true; // function/job title is optional in most forms
  return value.length <= FUNCTION_MAX_LENGTH;
}

