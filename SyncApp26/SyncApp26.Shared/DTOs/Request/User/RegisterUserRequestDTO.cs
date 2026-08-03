using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.User
{
    public class RegisterUserRequestDTO
    {
        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "First name must contain letters only (spaces, hyphens and apostrophes allowed).")]
        public required string FirstName { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "Last name must contain letters only (spaces, hyphens and apostrophes allowed).")]
        public required string LastName { get; set; }

        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}