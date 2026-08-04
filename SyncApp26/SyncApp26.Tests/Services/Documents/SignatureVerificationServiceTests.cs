using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Documents
{
    public class SignatureVerificationServiceTests : IDisposable
    {
        private const string TestKey = "test-signing-key-for-signature-verification-tests";

        private readonly SqliteContextFixture _dbFixture = new();
        private readonly Mock<ICryptographyService> _cryptographyServiceMock = new();
        private readonly HmacSignatureService _hmacService;

        public SignatureVerificationServiceTests()
        {
            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            _hmacService = new HmacSignatureService(keyProviderMock.Object);
            _cryptographyServiceMock.Setup(c => c.SignDataAsync(It.IsAny<string>())).ReturnsAsync("rsa-proof");
        }

        public void Dispose() => _dbFixture.Dispose();

        // Signatures are created through the already-tested DocumentService so the chain/HMAC
        // fixtures reflect real production output, not a hand-rolled approximation of it.
        private DocumentService CreateDocumentService() =>
            new(_dbFixture.Context, _cryptographyServiceMock.Object, _hmacService);

        private SignatureVerificationService CreateVerificationService() =>
            new(_dbFixture.Context, _hmacService);

        private Function SeedFunction(string name)
        {
            var function = new Function { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Functions.Add(function);
            _dbFixture.Context.SaveChanges();
            return function;
        }

        private User SeedUser(string firstName, string lastName, Function function, Guid? assignedToId = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                PersonalId = Guid.NewGuid().ToString(),
                FunctionId = function.Id,
                AssignedToId = assignedToId,
                Role = UserRole.BasicUser,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        private UserDocument SeedDocument(User owner, string documentType, string status)
        {
            var doc = new UserDocument
            {
                Id = Guid.NewGuid(),
                UserId = owner.Id,
                User = owner,
                DocumentType = documentType,
                Status = status,
                GeneratedAt = DateTime.UtcNow,
                DocumentHash = "seed-hash"
            };
            _dbFixture.Context.UserDocuments.Add(doc);
            _dbFixture.Context.SaveChanges();
            return doc;
        }

        private SignatureRecord SignDocument(DocumentService docService, UserDocument doc, User signer, string signerRole = "User")
        {
            docService.UpdateDocumentSignatureAsync(doc.Id, signer.Id, signerRole, "Draw", "sig-data", "1.2.3.4")
                .GetAwaiter().GetResult();
            return _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
        }

        private PeriodicTraining SeedTraining(User owner, UserDocument doc, string material, decimal duration, DateTime trainingDate)
        {
            var training = new PeriodicTraining
            {
                Id = Guid.NewGuid(),
                UserId = owner.Id,
                UserDocumentId = doc.Id,
                DocumentType = doc.DocumentType,
                MaterialTaught = material,
                DurationHours = duration,
                TrainingDate = trainingDate,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.PeriodicTrainings.Add(training);
            _dbFixture.Context.SaveChanges();
            return training;
        }

        // Re-signs the same document+role a second time — CreateSignatureRecordAsync always fires
        // regardless of whether the flat PeriodicTraining signature field was already set, so this
        // produces a second SignatureRecord in the same (PeriodicTrainingId, SignerRole) slot
        // without needing the full edit-then-invalidate production flow. Identifies the new record
        // by exclusion (which id wasn't there before), not by Version or timestamp ordering — both
        // records share the same schema Version, so neither reliably distinguishes them.
        private SignatureRecord SignDocumentAgain(DocumentService docService, UserDocument doc, User signer, string signerRole = "User")
        {
            var existingIds = _dbFixture.Context.SignatureRecords
                .Where(r => r.UserDocumentId == doc.Id)
                .Select(r => r.Id)
                .ToHashSet();

            docService.UpdateDocumentSignatureAsync(doc.Id, signer.Id, signerRole, "Draw", "sig-data-2", "1.2.3.4")
                .GetAwaiter().GetResult();

            return _dbFixture.Context.SignatureRecords
                .Single(r => r.UserDocumentId == doc.Id && !existingIds.Contains(r.Id));
        }

        // ───────────────────────── GetVerificationStatusAsync ─────────────────────────

        [Fact]
        public async Task GetVerificationStatusAsync_UnknownId_ReturnsNull()
        {
            var service = CreateVerificationService();

            var result = await service.GetVerificationStatusAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_UntamperedFirstSignature_ReturnsValid()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var record = SignDocument(docService, doc, owner);

            var status = await CreateVerificationService().GetVerificationStatusAsync(record.Id);

            Assert.NotNull(status);
            Assert.Equal(record.Id, status!.SignatureId);
            Assert.Equal(owner.Id, status.SignerUserId);
            Assert.Equal("Valid", status.Status);
            Assert.True(status.IsHashValid);
            Assert.True(status.IsChainValid);
            Assert.False(status.IsLegacy);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_TamperedSnapshotAfterSigning_ReturnsInvalid()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var record = SignDocument(docService, doc, owner);

            record.SignerFullNameSnapshot = "Forged Name";
            _dbFixture.Context.SaveChanges();

            var status = await CreateVerificationService().GetVerificationStatusAsync(record.Id);

            Assert.Equal("Invalid", status!.Status);
            Assert.False(status.IsHashValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_TamperedHmac_ReturnsInvalid()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var record = SignDocument(docService, doc, owner);

            record.SignatureHmac = new string('f', 64);
            _dbFixture.Context.SaveChanges();

            var status = await CreateVerificationService().GetVerificationStatusAsync(record.Id);

            Assert.Equal("Invalid", status!.Status);
            Assert.False(status.IsHashValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_SecondSignatureInChain_ValidatesAgainstFirst()
        {
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner1 = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var owner2 = SeedUser("Ion", "Vasile", employeeFunction, manager.Id);
            var doc1 = SeedDocument(owner1, "SU", "PendingManager");
            var doc2 = SeedDocument(owner2, "SU", "PendingManager");

            SignDocument(docService, doc1, manager, signerRole: "Manager");
            var secondRecord = SignDocument(docService, doc2, manager, signerRole: "Manager");

            var status = await CreateVerificationService().GetVerificationStatusAsync(secondRecord.Id);

            Assert.Equal("Valid", status!.Status);
            Assert.True(status.IsHashValid);
            Assert.True(status.IsChainValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_ForgedPreviousHashWithMatchingHmac_ReturnsChainBroken()
        {
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner1 = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var owner2 = SeedUser("Ion", "Vasile", employeeFunction, manager.Id);
            var doc1 = SeedDocument(owner1, "SU", "PendingManager");
            var doc2 = SeedDocument(owner2, "SU", "PendingManager");

            SignDocument(docService, doc1, manager, signerRole: "Manager");
            var secondRecord = SignDocument(docService, doc2, manager, signerRole: "Manager");

            // Simulate an attacker who knows the signing key: relink the record to a
            // nonexistent predecessor and recompute a self-consistent HMAC over the forgery.
            // The hash alone can't catch this — only the chain-continuity check can.
            var forgedPreviousHash = new string('a', 64);
            var forgedInput = new SignatureCanonicalInput(
                secondRecord.SignerUserId,
                secondRecord.SignerFullNameSnapshot,
                secondRecord.SignerPositionSnapshot,
                secondRecord.SignerBadgeNumberSnapshot,
                secondRecord.MaterialTaughtSnapshot,
                secondRecord.DurationHoursSnapshot,
                secondRecord.TrainingDateSnapshot,
                secondRecord.SignedAt,
                forgedPreviousHash,
                secondRecord.Version);
            var forgedCanonical = SignatureCanonicalSerializer.Serialize(forgedInput);
            secondRecord.PreviousSignatureHash = forgedPreviousHash;
            secondRecord.SignatureHmac = await _hmacService.ComputeHmacAsync(forgedCanonical);
            _dbFixture.Context.SaveChanges();

            var status = await CreateVerificationService().GetVerificationStatusAsync(secondRecord.Id);

            Assert.Equal("ChainBroken", status!.Status);
            Assert.True(status.IsHashValid);
            Assert.False(status.IsChainValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_LegacyUnverifiedRecord_ReturnsLegacy()
        {
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "Completed");

            var legacyRecord = new SignatureRecord
            {
                Id = Guid.NewGuid(),
                UserDocumentId = doc.Id,
                SignerRole = "User",
                SignerUserId = owner.Id,
                SignerFullNameSnapshot = "Adela Popescu",
                SignerPositionSnapshot = "Operator",
                SignatureMethod = "Draw",
                SignatureData = "legacy-data",
                SignedAt = DateTimeOffset.UtcNow,
                SignatureHmac = null,
                IsLegacyUnverified = true
            };
            _dbFixture.Context.SignatureRecords.Add(legacyRecord);
            _dbFixture.Context.SaveChanges();

            var status = await CreateVerificationService().GetVerificationStatusAsync(legacyRecord.Id);

            Assert.Equal("Legacy", status!.Status);
            Assert.True(status.IsLegacy);
            Assert.False(status.IsHashValid);
            Assert.False(status.IsChainValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_OnlyVersion_TrainingContentEditedAfterSigning_ReturnsInvalid()
        {
            // Regression guard: a single-version signature must still fail verification when its
            // linked training content is edited afterwards — this is the pre-existing, intentional
            // "force a re-sign" behavior and must survive the historical-vs-live fix below.
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var record = SignDocument(docService, doc, owner);

            training.MaterialTaught = "Norme SSM v2 - schimbat dupa semnare";
            _dbFixture.Context.SaveChanges();

            var status = await CreateVerificationService().GetVerificationStatusAsync(record.Id);

            Assert.Equal("Invalid", status!.Status);
            Assert.False(status.IsHashValid);
        }

        [Fact]
        public async Task GetVerificationStatusAsync_OlderSignature_StaysValidAfterTrainingEditedFollowingNewerSignature()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var olderRecord = SignDocument(docService, doc, owner);
            var newerRecord = SignDocumentAgain(docService, doc, owner);

            // Both signed under today's (only) HMAC schema — Version doesn't distinguish them.
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, olderRecord.Version);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, newerRecord.Version);

            // Edited after BOTH signatures — the newer (most recent) signature must detect this, but the
            // older, superseded version must keep verifying against what it actually signed.
            var training = _dbFixture.Context.PeriodicTrainings.Single(t => t.UserDocumentId == doc.Id);
            training.MaterialTaught = "Norme SSM v2 - schimbat dupa ambele semnaturi";
            _dbFixture.Context.SaveChanges();

            var olderStatus = await CreateVerificationService().GetVerificationStatusAsync(olderRecord.Id);
            var newerStatus = await CreateVerificationService().GetVerificationStatusAsync(newerRecord.Id);

            Assert.Equal("Valid", olderStatus!.Status);
            Assert.True(olderStatus.IsHashValid);

            Assert.Equal("Invalid", newerStatus!.Status);
            Assert.False(newerStatus.IsHashValid);
        }

        // ───────────────────────── GetVerificationStatusBatchAsync ─────────────────────────

        [Fact]
        public async Task GetVerificationStatusBatchAsync_MixOfKnownAndUnknownIds_ReturnsCorrectStatuses()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var record = SignDocument(docService, doc, owner);
            var unknownId = Guid.NewGuid();

            var results = await CreateVerificationService()
                .GetVerificationStatusBatchAsync(new[] { record.Id, unknownId });

            Assert.Equal(2, results.Count);
            Assert.Equal("Valid", results.Single(r => r.SignatureId == record.Id).Status);
            var notFound = results.Single(r => r.SignatureId == unknownId);
            Assert.Equal("NotFound", notFound.Status);
            Assert.Equal(Guid.Empty, notFound.SignerUserId);
        }

        [Fact]
        public async Task GetVerificationStatusBatchAsync_DuplicateIds_ReturnsOneResultPerDistinctId()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var record = SignDocument(docService, doc, owner);

            var results = await CreateVerificationService()
                .GetVerificationStatusBatchAsync(new[] { record.Id, record.Id });

            Assert.Single(results);
        }

        [Fact]
        public async Task GetVerificationStatusBatchAsync_MultipleSigners_EachResolvesItsOwnChain()
        {
            var docService = CreateDocumentService();
            var functionA = SeedFunction("Operator");
            var functionB = SeedFunction("Tehnician");
            var ownerA = SeedUser("Adela", "Popescu", functionA);
            var ownerB = SeedUser("Bogdan", "Ionescu", functionB);
            var docA = SeedDocument(ownerA, "SU", "PendingUser");
            var docB = SeedDocument(ownerB, "SU", "PendingUser");
            var recordA = SignDocument(docService, docA, ownerA);
            var recordB = SignDocument(docService, docB, ownerB);

            var results = await CreateVerificationService()
                .GetVerificationStatusBatchAsync(new[] { recordA.Id, recordB.Id });

            Assert.All(results, r => Assert.Equal("Valid", r.Status));
            Assert.All(results, r => Assert.True(r.IsChainValid));
        }

        [Fact]
        public async Task GetVerificationStatusBatchAsync_OldAndNewSignatureInSameSlot_EachEvaluatedIndependently()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var olderRecord = SignDocument(docService, doc, owner);
            var newerRecord = SignDocumentAgain(docService, doc, owner);

            var training = _dbFixture.Context.PeriodicTrainings.Single(t => t.UserDocumentId == doc.Id);
            training.MaterialTaught = "Norme SSM v2 - schimbat dupa ambele semnaturi";
            _dbFixture.Context.SaveChanges();

            var results = await CreateVerificationService()
                .GetVerificationStatusBatchAsync(new[] { olderRecord.Id, newerRecord.Id });

            Assert.Equal("Valid", results.Single(r => r.SignatureId == olderRecord.Id).Status);
            Assert.Equal("Invalid", results.Single(r => r.SignatureId == newerRecord.Id).Status);
        }

        // ───────────────────────── GetSignatureHistoryForTrainingAsync ─────────────────────────

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_UnknownTraining_ReturnsNull()
        {
            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_NoSignaturesYet_ReturnsEmptyVersionsByRole()
        {
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));

            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(training.Id);

            Assert.NotNull(result);
            Assert.Equal(owner.Id, result!.UserId);
            Assert.Empty(result.VersionsByRole);
        }

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_SingleSignature_ReturnsOneEntryMarkedMostRecent()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var record = SignDocument(docService, doc, owner);

            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(training.Id);

            var userVersions = result!.VersionsByRole["User"];
            Assert.Single(userVersions);
            Assert.Equal(record.Id, userVersions[0].SignatureId);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, userVersions[0].Version);
            Assert.True(userVersions[0].IsMostRecentSignature);
            Assert.Equal("Valid", userVersions[0].Status);
        }

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_TwoSignaturesSameRole_OrderedAscendingWithOnlyMostRecentMarked()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var older = SignDocument(docService, doc, owner);
            var newer = SignDocumentAgain(docService, doc, owner);

            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(training.Id);

            var userVersions = result!.VersionsByRole["User"];
            Assert.Equal(2, userVersions.Count);
            Assert.Equal(older.Id, userVersions[0].SignatureId);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, userVersions[0].Version);
            Assert.False(userVersions[0].IsMostRecentSignature);
            Assert.Equal(newer.Id, userVersions[1].SignatureId);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, userVersions[1].Version);
            Assert.True(userVersions[1].IsMostRecentSignature);
        }

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_MultipleRoles_GroupsIndependently()
        {
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var doc = SeedDocument(owner, "SU", "PendingManager");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var userRecord = SignDocument(docService, doc, owner);
            await docService.UpdateDocumentSignatureAsync(doc.Id, manager.Id, signerRole: "Manager", "Draw", "sig-data", "1.2.3.4");
            var managerRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id && r.SignerRole == "Manager");

            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(training.Id);

            Assert.Equal(2, result!.VersionsByRole.Count);
            Assert.Single(result.VersionsByRole["User"]);
            Assert.Single(result.VersionsByRole["Manager"]);
            Assert.Equal(userRecord.Id, result.VersionsByRole["User"][0].SignatureId);
            Assert.Equal(managerRecord.Id, result.VersionsByRole["Manager"][0].SignatureId);
        }

        // ───────────────────────── Mixed schema versions ─────────────────────────

        // Hand-builds a SignatureRecord with a real, correctly-computed HMAC under an explicit
        // schema version — the normal signing flow always stamps
        // SignatureCanonicalSerializer.CurrentVersion, so this is the only way to simulate "a
        // signature made after a hypothetical schema bump" without waiting for a real one to exist.
        private async Task<SignatureRecord> SeedManuallySignedRecordAsync(UserDocument doc, PeriodicTraining? training,
            string signerRole, User signer, string position, int version)
        {
            var signedAt = DateTimeOffset.UtcNow;
            var fullName = $"{signer.FirstName} {signer.LastName}";
            var input = new SignatureCanonicalInput(
                signer.Id,
                fullName,
                position,
                signer.BadgeNumber,
                training?.MaterialTaught,
                training?.DurationHours,
                training?.TrainingDate,
                signedAt,
                PreviousSignatureHash: null,
                Version: version);
            var hmac = await _hmacService.ComputeHmacAsync(SignatureCanonicalSerializer.Serialize(input));

            var record = new SignatureRecord
            {
                Id = Guid.NewGuid(),
                UserDocumentId = doc.Id,
                PeriodicTrainingId = training?.Id,
                SignerRole = signerRole,
                SignerUserId = signer.Id,
                SignerFullNameSnapshot = fullName,
                SignerPositionSnapshot = position,
                SignerBadgeNumberSnapshot = signer.BadgeNumber,
                SignatureMethod = "Draw",
                SignatureData = "sig-manual",
                MaterialTaughtSnapshot = training?.MaterialTaught,
                DurationHoursSnapshot = training?.DurationHours,
                TrainingDateSnapshot = training?.TrainingDate,
                SignedAt = signedAt,
                PreviousSignatureHash = null,
                SignatureHmac = hmac,
                IsLegacyUnverified = false,
                Version = version
            };
            _dbFixture.Context.SignatureRecords.Add(record);
            _dbFixture.Context.SaveChanges();
            return record;
        }

        [Fact]
        public async Task GetVerificationStatusAsync_MixedSchemaVersionsOnSameDocument_BothVerifyAsValid()
        {
            // Proves the per-record Version dispatch works on a real mix, not just in isolation:
            // one signature made under today's schema through the normal signing flow, another
            // hand-built under the older V1 (as production rows signed before the bump still are),
            // both on the SAME document — each must verify using its own stored Version.
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var doc = SeedDocument(owner, "SU", "PendingManager");
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var currentRecord = SignDocument(docService, doc, owner);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, currentRecord.Version);

            var legacyV1Record = await SeedManuallySignedRecordAsync(doc, training, "Manager", manager, "Sef Echipa", version: 1);

            var currentStatus = await CreateVerificationService().GetVerificationStatusAsync(currentRecord.Id);
            var legacyStatus = await CreateVerificationService().GetVerificationStatusAsync(legacyV1Record.Id);

            Assert.Equal("Valid", currentStatus!.Status);
            Assert.True(currentStatus.IsHashValid);
            Assert.Equal("Valid", legacyStatus!.Status);
            Assert.True(legacyStatus.IsHashValid);
        }

        [Fact]
        public async Task GetVerificationStatusBatchAsync_MixedSchemaVersions_BothVerifyIndependently()
        {
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var doc = SeedDocument(owner, "SU", "PendingManager");
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var currentRecord = SignDocument(docService, doc, owner);
            var legacyV1Record = await SeedManuallySignedRecordAsync(doc, training, "Manager", manager, "Sef Echipa", version: 1);

            var results = await CreateVerificationService()
                .GetVerificationStatusBatchAsync(new[] { currentRecord.Id, legacyV1Record.Id });

            Assert.Equal("Valid", results.Single(r => r.SignatureId == currentRecord.Id).Status);
            Assert.Equal("Valid", results.Single(r => r.SignatureId == legacyV1Record.Id).Status);
        }

        [Fact]
        public async Task GetSignatureHistoryForTrainingAsync_MixedSchemaVersions_BothReportValidStatus()
        {
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var doc = SeedDocument(owner, "SU", "PendingManager");
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var currentRecord = SignDocument(docService, doc, owner);
            var legacyV1Record = await SeedManuallySignedRecordAsync(doc, training, "Manager", manager, "Sef Echipa", version: 1);

            var result = await CreateVerificationService().GetSignatureHistoryForTrainingAsync(training.Id);

            var userEntry = result!.VersionsByRole["User"].Single(v => v.SignatureId == currentRecord.Id);
            var managerEntry = result.VersionsByRole["Manager"].Single(v => v.SignatureId == legacyV1Record.Id);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, userEntry.Version);
            Assert.Equal("Valid", userEntry.Status);
            Assert.Equal(1, managerEntry.Version);
            Assert.Equal("Valid", managerEntry.Status);
        }

        // ───────────────────────── GetVerificationStatusForUsersAsync ─────────────────────────

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_EmptyList_ReturnsEmptyDictionary()
        {
            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(Array.Empty<Guid>());

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_EmployeeWithNoSignaturesYet_ReturnsEmptyListNotMissing()
        {
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            SeedDocument(owner, "SU", "PendingUser"); // generated but not signed yet

            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { owner.Id });

            Assert.True(result.ContainsKey(owner.Id));
            Assert.Empty(result[owner.Id]);
        }

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_UnknownUserId_ReturnsEmptyListNotError()
        {
            var unknownId = Guid.NewGuid();

            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { unknownId });

            Assert.True(result.ContainsKey(unknownId));
            Assert.Empty(result[unknownId]);
        }

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_ManagerCountersignature_AttributedToEmployeeNotSigner()
        {
            // Scoped by the document's owner, not the signer — requesting only the employee must
            // surface BOTH their own signature and the manager's countersignature on their
            // document, and nothing should appear under the manager's own id.
            var docService = CreateDocumentService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction, manager.Id);
            var doc = SeedDocument(owner, "SU", "PendingManager");

            var userRecord = SignDocument(docService, doc, owner, signerRole: "User");
            await docService.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Draw", "sig-data", "1.2.3.4");
            var managerRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id && r.SignerRole == "Manager");

            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { owner.Id });

            Assert.True(result.ContainsKey(owner.Id));
            Assert.Equal(2, result[owner.Id].Count);
            Assert.Contains(result[owner.Id], s => s.SignatureId == userRecord.Id && s.Status == "Valid");
            Assert.Contains(result[owner.Id], s => s.SignatureId == managerRecord.Id && s.Status == "Valid");
            Assert.False(result.ContainsKey(manager.Id));
        }

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_MultipleEmployees_EachGetsOwnStatuses()
        {
            var docService = CreateDocumentService();
            var functionA = SeedFunction("Operator");
            var functionB = SeedFunction("Tehnician");
            var ownerA = SeedUser("Adela", "Popescu", functionA);
            var ownerB = SeedUser("Bogdan", "Ionescu", functionB);
            var docA = SeedDocument(ownerA, "SU", "PendingUser");
            var docB = SeedDocument(ownerB, "SU", "PendingUser");
            var recordA = SignDocument(docService, docA, ownerA);
            var recordB = SignDocument(docService, docB, ownerB);

            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { ownerA.Id, ownerB.Id });

            Assert.Single(result[ownerA.Id]);
            Assert.Equal(recordA.Id, result[ownerA.Id][0].SignatureId);
            Assert.Single(result[ownerB.Id]);
            Assert.Equal(recordB.Id, result[ownerB.Id][0].SignatureId);
        }

        [Fact]
        public async Task GetVerificationStatusForUsersAsync_TrainingContentEditedAfterSigning_ReportsInvalidForThatEmployee()
        {
            var docService = CreateDocumentService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var record = SignDocument(docService, doc, owner);

            training.MaterialTaught = "Norme SSM v2 - schimbat";
            _dbFixture.Context.SaveChanges();

            var result = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { owner.Id });

            Assert.Equal("Invalid", result[owner.Id].Single(s => s.SignatureId == record.Id).Status);
        }
    }
}
