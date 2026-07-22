using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
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

            if (!isUser && !isManager && !callerIsAdmin)
                return new SigningTokenResult { Forbidden = true };

            if (isUser && document.UserSignedAt != null)
                return new SigningTokenResult { ErrorMessage = "User already signed this document." };

            if (isManager && document.ManagerSignedAt != null)
                return new SigningTokenResult { ErrorMessage = "Manager already signed this document." };

            if (isManager && !callerIsAdmin && document.UserSignedAt == null)
                return new SigningTokenResult { ErrorMessage = "Employee must sign first before manager can countersign." };

            if (isUser && document.Status != "PendingUser")
                return new SigningTokenResult { ErrorMessage = "User signature not required at this time." };

            if (isManager && !callerIsAdmin && document.Status != "PendingManager")
                return new SigningTokenResult { ErrorMessage = "Manager signature not required at this time." };

            if (callerIsAdmin && !isUser && !isManager)
            {
                if (document.Status != "PendingAdmin")
                    return new SigningTokenResult { ErrorMessage = "Admin signature not required at this time." };
                if (document.DocumentType?.ToUpperInvariant() != "SSM")
                    return new SigningTokenResult { ErrorMessage = "Admin only signs SSM documents." };
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
            bool isManagerSigning = !signerIsAdmin && (document?.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, signatureToken.Email, StringComparison.OrdinalIgnoreCase));
            bool isAdminSigning = signerIsAdmin && document?.DocumentType?.ToUpperInvariant() == "SSM";

            return new SigningContextResult
            {
                Success = true,
                DocumentId = signatureToken.DocumentId,
                DocumentName = signatureToken.DocumentName,
                Email = signatureToken.Email,
                DocumentType = document?.DocumentType,
                IsManagerSigning = isManagerSigning,
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

            bool signerIsAdmin = signerUserFromToken?.Role == UserRole.Admin;
            bool isLineManager = !signerIsAdmin && (document.User?.AssignedTo != null &&
                string.Equals(document.User.AssignedTo.Email, tokenEntity.Email, StringComparison.OrdinalIgnoreCase));
            bool isUserSignature = !signerIsAdmin && !isLineManager;

            if (isUserSignature && document.UserSignedAt != null)
                return new ConsumeSigningTokenResult { ErrorMessage = "User already signed this document." };

            if (isLineManager && document.UserSignedAt == null)
                return new ConsumeSigningTokenResult { ErrorMessage = "Employee must sign this document first." };

            if (isLineManager && document.ManagerSignedAt != null)
                return new ConsumeSigningTokenResult { ErrorMessage = "Manager already signed this document." };

            if (signerIsAdmin)
            {
                if (document.UserSignedAt == null || document.ManagerSignedAt == null)
                    return new ConsumeSigningTokenResult { ErrorMessage = "Both employee and manager must sign before admin can verify." };
                if (document.DocumentType?.ToUpperInvariant() != "SSM")
                    return new ConsumeSigningTokenResult { ErrorMessage = "Admin only signs SSM documents." };
                if (document.Status != "PendingAdmin")
                    return new ConsumeSigningTokenResult { ErrorMessage = "Document is not pending admin signature." };
            }

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);
            if (!isValidAndConsumed)
                return new ConsumeSigningTokenResult { ErrorMessage = "Token could not be consumed." };

            var periodicTrainingId = request.PeriodicTrainingId ?? tokenEntity.PeriodicTrainingId;

            await _documentService.UpdateDocumentSignatureAsync(
                document.Id,
                signerUserFromToken.Id,
                isUserSignature,
                request.SignatureMethod,
                request.SignatureData,
                request.IpAddress,
                signerIsAdmin,
                periodicTrainingId
            );

            string? managerEmail = null;
            string? managerNotificationDocumentName = null;
            string? managerNotificationToken = null;

            if (isUserSignature && document.User?.AssignedTo != null)
            {
                var manager = document.User.AssignedTo;
                managerNotificationDocumentName = $"{document.DocumentType} Document (Manager Approval)";
                managerNotificationToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                    manager.Email,
                    document.Id,
                    managerNotificationDocumentName,
                    tokenEntity.PeriodicTrainingId);
                managerEmail = manager.Email;
            }

            int bulkCount = 0;
            if (request.BulkSign && (isLineManager || signerIsAdmin) && signerUserFromToken != null)
            {
                bulkCount = await _documentService.BulkSignDocumentsAsync(
                    signerIsAdmin, signerUserFromToken.Id,
                    request.SignatureMethod, request.SignatureData, request.IpAddress);
            }

            return new ConsumeSigningTokenResult
            {
                Success = true,
                TotalSigned = bulkCount + 1,
                ManagerEmail = managerEmail,
                ManagerNotificationDocumentName = managerNotificationDocumentName,
                ManagerNotificationToken = managerNotificationToken
            };
        }
    }
}
