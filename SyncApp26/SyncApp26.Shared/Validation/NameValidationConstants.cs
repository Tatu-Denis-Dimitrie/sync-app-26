namespace SyncApp26.Shared.Validation
{
    public static class NameValidationConstants
    {
        public const int NameMaxLength = 100;

        // Unicode letters, optionally separated by single spaces/hyphens/apostrophes;
        // must start and end with a letter (or be a single letter). Covers diacritics
        // (e.g. Romanian ă â î ș ț) and compound/hyphenated names (Anne-Marie, O'Brien).
        public const string NamePattern = @"^\p{L}$|^\p{L}[\p{L}\s'-]*\p{L}$";

        public const int FunctionMaxLength = 100;
    }
}
