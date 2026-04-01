using System;
using System.Collections.Generic;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Shared.DTOs.Response.User
{
    public class UserSSMSUFormDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PersonalId { get; set; } = string.Empty;
        public string? DepartmentName { get; set; }
        public string? FunctionName { get; set; }
        public string? RoleName { get; set; }
        public string? ManagerFirstName { get; set; }
        public string? ManagerLastName { get; set; }
        public string? ManagerFunctionName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Address { get; set; }
        public string? BloodGroup { get; set; }
        public string? BadgeNumber { get; set; }
        public string? Education { get; set; }
        public string? Qualifications { get; set; }
        public string? CommuteRoute { get; set; }
        public int? CommuteDurationMinutes { get; set; }
        public List<InitialTrainingEntryDTO> InitialTrainings { get; set; } = new();
        public string? AdmittedByName { get; set; }
        public string? AdmittedByFunction { get; set; }
        public DateTime? AdmittedDate { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? LatestInstructorSignature { get; set; }
        public string? LatestInstructorSignatureMethod { get; set; }
        public string? LatestVerifierSignature { get; set; }
        public string? LatestVerifierSignatureMethod { get; set; }
    }
}
