using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
    // Workflow: User -> Manager (AssignedTo) -> Instructor -> Completed. The Instructor slot belongs
    // to the SSM/SU officer for the document's type (SsmOfficer for SSM, SuOfficer for SU) — not a
    // per-row InstructorId pick, and admin has no standing in this chain at all.
    public class DocumentSigningService : IDocumentSigningService
    {
        private readonly IDocumentService _documentService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IUserService _userService;
        private readonly IStringLocalizer _localizer;

        public DocumentSigningService(
            IDocumentService documentService,
            IDocumentSignatureService documentSignatureService,
            IUserService userService,
            ILocalizationService localizationService)
        {
            _documentService = documentService;
            _documentSignatureService = documentSignatureService;
            _userService = userService;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Documents);
        }

        // Names both the problem and the way out: the step isn't blocked forever, it just needs a
        // different person. Which officer role that is depends on the document type.
        private string SelfCountersignMessage(bool isSsm) =>
            _localizer["signing.selfCountersign", isSsm ? "SSM" : "SU"];

        public async Task<SigningTokenResult> RequestSigningTokenAsync(UserDocument document, User caller)
        {
            bool isSsm = document.DocumentType?.ToUpperInvariant() == "SSM";
            bool isUser = document.UserId == caller.Id;
            // Separation of duties: the trainee never countersigns their own document, so both
            // countersigning slots stay closed to them even when they legitimately hold these roles
            // for other employees. Someone else holding both roles still fills both slots normally.
            bool isManager = !isUser && document.User?.AssignedToId == caller.Id;
            // The officer for this document's type takes the Instructor's place in the chain — no
            // per-row InstructorId match anymore, and no admin override.
            bool isInstructor = !isUser && await _userService.IsInRoleAsync(caller.Id, isSsm ? Roles.SsmOfficer : Roles.SuOfficer);

            if (!isUser && !isManager && !isInstructor)
                return new SigningTokenResult { Forbidden = true };

            switch (document.Status)
            {
                case "PendingUser":
                    if (isUser && document.UserSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = _localizer["signing.userAlreadySigned"] };
                    if (!isUser)
                        return new SigningTokenResult { ErrorMessage = _localizer["signing.userSignatureNotRequired"] };
                    break;

                case "PendingManager":
                    if (isUser && document.UserSignedAt != null)
                        return new SigningTokenResult { ErrorMessage = _localizer["signing.userAlreadySigned"] };
                    if (isUser)
                        return new SigningTokenResult { ErrorMessage = SelfCountersignMessage(isSsm) };
                    if (!isManager)
                        return new SigningTokenResult { ErrorMessage = _localizer["signing.managerSignatureNotRequired"] };
                    break;

                case "PendingInstructor":
                    if (isUser)
                        return new SigningTokenResult { ErrorMessage = SelfCountersignMessage(isSsm) };
                    // isInstructor takes priority: a dual-role manager+officer who already signed as
                    // manager must still be able to sign as instructor. Only someone who is manager
                    // but NOT the instructor hits the "already signed" message below.
                    if (!isInstructor)
                    {
                        if (isManager && document.ManagerSignedAt != null)
                            return new SigningTokenResult { ErrorMessage = _localizer["signing.managerAlreadySigned"] };
                        return new SigningTokenResult { ErrorMessage = _localizer["signing.instructorSignatureNotRequired"] };
                    }
                    break;

                default:
                    return new SigningTokenResult { ErrorMessage = _localizer["signing.documentNotAwaitingSignature"] };
            }

            var currentRowId = await _documentService.GetCurrentTrainingIdForDocumentAsync(document.Id);
            var token = await _documentSignatureService.GenerateSignatureTokenAsync(caller.Email, document.Id, $"{document.DocumentType} Document", currentRowId);

            return new SigningTokenResult { Success = true, Token = token };
        }

        public async Task<SigningContextResult> GetSigningContextAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return new SigningContextResult { ErrorMessage = _localizer["signing.tokenRequired"] };

            var signatureToken = await _documentSignatureService.ValidateTokenAsync(token);
            if (signatureToken == null)
                return new SigningContextResult { ErrorMessage = _localizer["signing.invalidOrExpiredToken"] };

            var document = await _documentService.GetDocumentByIdAsync(signatureToken.DocumentId);
            var signerUser = await _userService.GetUserByEmailAsync(signatureToken.Email);

            bool isManagerSigning = false;
            bool isInstructorSigning = false;

            if (document != null && signerUser != null)
            {
                bool isSsm = document.DocumentType?.ToUpperInvariant() == "SSM";
                // Same separation-of-duties rule as the gate below, so the signing page never offers
                // a countersigning button that consuming the token would refuse.
                bool isUser = document.UserId == signerUser.Id;
                bool isManager = !isUser && document.User?.AssignedToId == signerUser.Id;
                // The officer for this document's type takes the Instructor's place — no admin override.
                bool isInstructor = !isUser && await _userService.IsInRoleAsync(signerUser.Id, isSsm ? Roles.SsmOfficer : Roles.SuOfficer);

                switch (document.Status)
                {
                    case "PendingManager":
                        isManagerSigning = isManager;
                        break;
                    case "PendingInstructor":
                        isInstructorSigning = isInstructor;
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
                IsAdminSigning = false,
                PeriodicTrainingId = signatureToken.PeriodicTrainingId
            };
        }

        public async Task<ConsumeSigningTokenResult> ConsumeSigningTokenAsync(ConsumeSigningTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return new ConsumeSigningTokenResult { ErrorMessage = _localizer["signing.tokenRequired"] };

            var tokenEntity = await _documentSignatureService.ValidateTokenAsync(request.Token);
            if (tokenEntity == null)
                return new ConsumeSigningTokenResult { ErrorMessage = _localizer["signing.tokenInvalidOrExpired"] };

            var document = await _documentService.GetDocumentByIdAsync(tokenEntity.DocumentId);
            if (document == null)
                return new ConsumeSigningTokenResult { ErrorMessage = _localizer["signing.documentNotFound"] };

            var signerUserFromToken = await _userService.GetUserByEmailAsync(tokenEntity.Email);
            if (signerUserFromToken == null)
                return new ConsumeSigningTokenResult { ErrorMessage = _localizer["signing.signerAccountNotFound"] };

            var periodicTrainingId = request.PeriodicTrainingId ?? tokenEntity.PeriodicTrainingId;

            bool isSsm = document.DocumentType?.ToUpperInvariant() == "SSM";
            bool isUser = document.UserId == signerUserFromToken.Id;
            // Separation of duties, enforced at the authoritative gate: a token alone proves nothing
            // about eligibility, so the trainee is refused the countersigning slots here even if one
            // was minted for them. Non-owners keep both slots, dual roles included.
            bool isManager = !isUser && document.User?.AssignedToId == signerUserFromToken.Id;
            // The officer for this document's type takes the Instructor's place in the chain — no
            // per-row InstructorId match anymore, and no admin override.
            bool isInstructor = !isUser && await _userService.IsInRoleAsync(signerUserFromToken.Id, isSsm ? Roles.SsmOfficer : Roles.SuOfficer);

            string? signerRole = document.Status switch
            {
                "PendingUser" when isUser => "User",
                "PendingManager" when isManager => "Manager",
                "PendingInstructor" when isInstructor => "Instructor",
                _ => null
            };

            if (signerRole == null)
            {
                // Distinguish "it's not your turn" from "it will never be your turn on this document".
                bool awaitingCountersignature = document.Status is "PendingManager" or "PendingInstructor";
                return new ConsumeSigningTokenResult
                {
                    ErrorMessage = isUser && awaitingCountersignature
                        ? SelfCountersignMessage(isSsm)
                        : _localizer["signing.notAwaitingYourSignature"]
                };
            }

            var isValidAndConsumed = await _documentSignatureService.ConsumeTokenAsync(request.Token);
            if (!isValidAndConsumed)
                return new ConsumeSigningTokenResult { ErrorMessage = _localizer["signing.tokenCouldNotBeConsumed"] };

            await _documentService.UpdateDocumentSignatureAsync(
                document.Id,
                signerUserFromToken.Id,
                signerRole,
                request.SignatureMethod,
                request.SignatureData,
                request.IpAddress,
                periodicTrainingId
            );

            // Notify everyone who needs to sign next in the chain — a single manager, or every
            // officer currently holding the role for this document's type (there can be more than one).
            var nextSignerNotifications = new List<SigningNotification>();

            if (signerRole == "User" && document.User?.AssignedTo != null)
            {
                var manager = document.User.AssignedTo;
                var docName = $"{document.DocumentType} Document (Manager Approval)";
                var managerToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                    manager.Email, document.Id, docName, periodicTrainingId);
                nextSignerNotifications.Add(new SigningNotification { Email = manager.Email, Token = managerToken, DocumentName = docName });
            }
            else if (signerRole == "Manager")
            {
                var officers = await _userService.GetUsersInRoleAsync(isSsm ? Roles.SsmOfficer : Roles.SuOfficer);
                var docName = $"{document.DocumentType} Document (Instructor Signature)";
                foreach (var officer in officers)
                {
                    var officerToken = await _documentSignatureService.GenerateSignatureTokenAsync(
                        officer.Email, document.Id, docName, periodicTrainingId);
                    nextSignerNotifications.Add(new SigningNotification { Email = officer.Email, Token = officerToken, DocumentName = docName });
                }
            }

            int bulkCount = 0;
            if (request.BulkSign && (signerRole == "Manager" || signerRole == "Instructor"))
            {
                bulkCount = await _documentService.BulkSignDocumentsAsync(
                    signerUserFromToken.Id, request.SignatureMethod, request.SignatureData, request.IpAddress);
            }

            return new ConsumeSigningTokenResult
            {
                Success = true,
                TotalSigned = bulkCount + 1,
                NextSignerNotifications = nextSignerNotifications
            };
        }
    }
}
