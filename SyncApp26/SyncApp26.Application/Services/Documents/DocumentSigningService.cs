using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
    // Workflow: User -> Manager (AssignedTo) -> Instructor (training.InstructorId) -> Admin (SSM
    // only) / Completed (SU, after Instructor)
    public class DocumentSigningService : IDocumentSigningService
    {
        private readonly IDocumentService _documentService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IUserService _userService;

        public DocumentSigningService(IDocumentService documentService, IDocumentSignatureService documentSignatureService, IUserService userService)
        {
            _documentService = documentService;
            _documentSignatureService = documentSignatureService;
            _userService = userService;
        }

        public async Task<SigningTokenResult> RequestSigningTokenAsync(UserDocument document, User caller, bool callerIsAdmin)
        {
            bool isUser = document.UserId == caller.Id;
            bool isManager = document.User?.AssignedToId == caller.Id;
            bool isInstructor = ResolveInstructorId(document, null) == caller.Id;

            if (!isUser && !isManager && !isInstructor && !callerIsAdmin)
                return new SigningTokenResult { Forbidden = true };

            switch (document.Status)
            {
                case "PendingUser":
                    if (isUser && document.UserSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = "User already signed this document." };
                    if (!isUser)
                        return new SigningTokenResult { ErrorMessage = "User signature not required at this time." };
                    break;

                case "PendingManager":
                    if (isUser && document.UserSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = "User already signed this document." };
                    if (!isManager && !callerIsAdmin)
                        return new SigningTokenResult { ErrorMessage = "Manager signature not required at this time." };
                    break;

                case "PendingInstructor":
                    if (isManager && document.ManagerSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = "Manager already signed this document." };
                    if (!isInstructor && !callerIsAdmin)
                        return new SigningTokenResult { ErrorMessage = "Instructor signature not required at this time." };
                    break;

                case "PendingAdmin":
                    if (isInstructor && document.InstructorSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = "Instructor already signed this document." };
                    if (!callerIsAdmin)
                        return new SigningTokenResult { ErrorMessage = "Admin signature not required at this time." };
                    if (document.DocumentType?.ToUpperInvariant() != "SSM")
                        return new SigningTokenResult { ErrorMessage = "Admin only signs SSM documents." };
                    break;

                default:
                    return new SigningTokenResult { ErrorMessage = "This document does not require a signature at this time." };
            }

            var currentRowId = await _documentService.GetCurrentTrainingIdForDocumentAsync(document.Id);
            var token = await _documentSignatureService.GenerateSignatureTokenAsync(caller.Email, document.Id, $"{document.DocumentType} Document", currentRowId);

            return new SigningTokenResult { Success = true, Token = token };
        }

        public async Task<SigningContextResult> GetSigningContextAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return new SigningContextResult { ErrorMessage = "Token is required." };

            var signatureToken = await _documentSignatureService.ValidateTokenAsync(token);
            if (signatureToken == null)
                return new SigningContextResult { ErrorMessage = "Invalid or expired token." };

            var document = await _documentService.GetDocumentByIdAsync(signatureToken.DocumentId);
            var signerUser = await _userService.GetUserByEmailAsync(signatureToken.Email);
            bool signerIsAdmin = signerUser?.Role == UserRole.Admin;

            bool isManagerSigning = false;
            bool isInstructorSigning = false;
            bool isAdminSigning = false;

            if (document != null && signerUser != null)
            {
                bool isManager = document.User?.AssignedToId == signerUser.Id;
                bool isInstructor = ResolveInstructorId(document, signatureToken.PeriodicTrainingId) == signerUser.Id;

                switch (document.Status)
                {
                    case "PendingManager":
                        isManagerSigning = isManager || (signerIsAdmin && !isInstructor);
                        break;
                    case "PendingInstructor":
                        isInstructorSigning = isInstructor || (signerIsAdmin && !isManager);
                        break;
                    case "PendingAdmin":
                        isAdminSigning = signerIsAdmin && document.DocumentType?.ToUpperInvariant() == "SSM";
                        break;
                }
            }

            return new SigningContextResult
            {
                Success = true,
                DocumentId = signatureToken.DocumentId,
                DocumentName = signatureToken.DocumentName,
                Email = signatureToken.Email,
                DocumentType = document?.DocumentType,
                IsManagerSigning = isManagerSigning,
                IsInstructorSigning = isInstructorSigning,
                IsAdminSigning = isAdminSigning,
                PeriodicTrainingId = signatureToken.PeriodicTrainingId
            };
        }

        public async Task<ConsumeSigningTokenResult> ConsumeSigningTokenAsync(ConsumeSigningTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return new ConsumeSigningTokenResult { ErrorMessage = "Token is required." };

            var tokenEntity = await _documentSignatureService.ValidateTokenAsync(request.Token);
            if (tokenEntity == null)
                return new ConsumeSigningTokenResult { ErrorMessage = "Token is invalid or expired." };

            var document = await _documentService.GetDocumentByIdAsync(tokenEntity.DocumentId);
            if (document == null)
                return new ConsumeSigningTokenResult { ErrorMessage = "Document not found." };

            var signerUserFromToken = await _userService.GetUserByEmailAsync(tokenEntity.Email);
            if (signerUserFromToken == null)
                return new ConsumeSigningTokenResult { ErrorMessage = "Signer account not found." };

            var periodicTrainingId = request.PeriodicTrainingId ?? tokenEntity.PeriodicTrainingId;

            bool signerIsAdmin = signerUserFromToken.Role == UserRole.Admin;
            bool isUser = document.UserId == signerUserFromToken.Id;
            bool isManager = document.User?.AssignedToId == signerUserFromToken.Id;
            bool isInstructor = ResolveInstructorId(document, periodicTrainingId) == signerUserFromToken.Id;

            string? signerRole = document.Status switch
            {
                "PendingUser" when isUser => "User",
                "PendingManager" when isManager || signerIsAdmin => "Manager",
                "PendingInstructor" when isInstructor || signerIsAdmin => "Instructor",
                "PendingAdmin" when signerIsAdmin => "Admin",
                _ => null
            };

            if (signerRole == null)
                return new ConsumeSigningTokenResult { ErrorMessage = "This document is not awaiting your signature at this time." };

            if (signerRole == "Admin" && document.DocumentType?.ToUpperInvariant() != "SSM")
                return new ConsumeSigningTokenResult { ErrorMessage = "Admin only signs SSM documents." };

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);
            if (!isValidAndConsumed)
                return new ConsumeSigningTokenResult { ErrorMessage = "Token could not be consumed." };

            await _documentService.UpdateDocumentSignatureAsync(
                document.Id,
                signerUserFromToken.Id,
                signerRole,
                request.SignatureMethod,
                request.SignatureData,
                request.IpAddress,
                periodicTrainingId
            );

            // Notify the next person in the chain
            string? nextEmail = null;
            string? nextNotificationDocumentName = null;
            string? nextNotificationToken = null;

            if (signerRole == "User" && document.User?.AssignedTo != null)
            {
                var manager = document.User.AssignedTo;
                nextNotificationDocumentName = $"{document.DocumentType} Document (Manager Approval)";
                nextNotificationToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                    manager.Email, document.Id, nextNotificationDocumentName, periodicTrainingId);
                nextEmail = manager.Email;
            }
            else if (signerRole == "Manager")
            {
                var instructorId = ResolveInstructorId(document, periodicTrainingId);
                var instructor = instructorId.HasValue ? await _userService.GetUserByIdAsync(instructorId.Value) : null;
                if (instructor != null)
                {
                    nextNotificationDocumentName = $"{document.DocumentType} Document (Instructor Signature)";
                    nextNotificationToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                        instructor.Email, document.Id, nextNotificationDocumentName, periodicTrainingId);
                    nextEmail = instructor.Email;
                }
            }

            int bulkCount = 0;
            if (request.BulkSign && (signerRole == "Manager" || signerRole == "Instructor" || signerIsAdmin))
            {
                bulkCount = await _documentService.BulkSignDocumentsAsync(
                    signerIsAdmin, signerUserFromToken.Id,
                    request.SignatureMethod, request.SignatureData, request.IpAddress);
            }

            return new ConsumeSigningTokenResult
            {
                Success = true,
                TotalSigned = bulkCount + 1,
                ManagerEmail = nextEmail,
                ManagerNotificationDocumentName = nextNotificationDocumentName,
                ManagerNotificationToken = nextNotificationToken
            };
        }

        // Resolves the training row's linked instructor account
        private static Guid? ResolveInstructorId(UserDocument document, Guid? periodicTrainingId)
        {
            var trainings = document.User?.PeriodicTrainings?.Where(pt => pt.UserDocumentId == document.Id);

            var training = periodicTrainingId.HasValue
                ? trainings?.FirstOrDefault(pt => pt.Id == periodicTrainingId.Value)
                : null;
            training ??= trainings?
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();

            return training?.InstructorId;
        }
    }
}
