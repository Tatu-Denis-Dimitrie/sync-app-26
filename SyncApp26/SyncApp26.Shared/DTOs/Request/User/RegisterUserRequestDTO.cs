using System.ComponentModel.DataAnnotations;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.User
{
    public class RegisterUserRequestDTO
    {
        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "firstName.pattern")]
        public required string FirstName { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "lastName.pattern")]
        public required string LastName { get; set; }

        public required string Email { get; set; }
        public required string Password { get; set; }
        public Language? PreferredLanguage { get; set; }
    }
}