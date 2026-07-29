using System.Security.Cryptography;
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
    public class DocumentServiceTests : IDisposable
    {
        private const string TestKey = "test-signing-key-for-document-service-tests";

        private readonly SqliteContextFixture _dbFixture = new();
        private readonly Mock<ICryptographyService> _cryptographyServiceMock = new();

        public void Dispose() => _dbFixture.Dispose();

        private DocumentService CreateService()
        {
            _cryptographyServiceMock.Setup(c => c.SignDataAsync(It.IsAny<string>())).ReturnsAsync("rsa-proof");

            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            var hmacService = new HmacSignatureService(keyProviderMock.Object);

            return new DocumentService(_dbFixture.Context, _cryptographyServiceMock.Object, hmacService);
        }

        // Recomputes the HMAC independently of DocumentService, so a passing assertion proves
        // the service captured the *correct* values, not just *some* non-null value.
        private static string ExpectedHmac(Guid signerUserId, string fullName, string position,
            string? material, decimal? duration, DateTime? trainingDate, DateTimeOffset signedAt)
        {
            var input = new SignatureCanonicalInput(signerUserId, fullName, position, material, duration, trainingDate, signedAt, null, SignatureCanonicalSerializer.CurrentVersion);
            var canonical = SignatureCanonicalSerializer.Serialize(input);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestKey));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        private Function SeedFunction(string name)
        {
            var function = new Function { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Functions.Add(function);
            _dbFixture.Context.SaveChanges();
            return function;
        }

        private User SeedUser(string firstName, string lastName, Function function, UserRole role = UserRole.BasicUser)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                PersonalId = Guid.NewGuid().ToString(),
                FunctionId = function.Id,
                Role = role,
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

        private PeriodicTraining SeedTraining(User owner, UserDocument doc, string material, decimal duration, DateTime trainingDate, Guid? instructorId = null)
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
                InstructorId = instructorId,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.PeriodicTrainings.Add(training);
            _dbFixture.Context.SaveChanges();
            return training;
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_UserSigns_CreatesSignatureRecordWithFrozenSnapshotAndCorrectHmac()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var result = await service.UpdateDocumentSignatureAsync(
                doc.Id, owner.Id, "User", "Draw", "signature-png-data", "1.2.3.4");

            Assert.True(result);

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("User", record.SignerRole);
            Assert.Equal(owner.Id, record.SignerUserId);
            Assert.Equal("Adela Popescu", record.SignerFullNameSnapshot);
            Assert.Equal("Operator", record.SignerPositionSnapshot);
            Assert.Equal("Norme SSM generale", record.MaterialTaughtSnapshot);
            Assert.Equal(2m, record.DurationHoursSnapshot);
            Assert.False(record.IsLegacyUnverified);
            Assert.Null(record.PreviousSignatureHash);
            Assert.False(string.IsNullOrEmpty(record.SignatureHmac));

            var expected = ExpectedHmac(owner.Id, "Adela Popescu", "Operator", "Norme SSM generale", 2m,
                new DateTime(2026, 1, 15), record.SignedAt);
            Assert.Equal(expected, record.SignatureHmac);
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_NameChangedAfterSigning_StoredSnapshotStaysFrozen()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Draw", "sig", "1.2.3.4");
            var hmacBeforeRename = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id).SignatureHmac;

            // A legitimate change to the live User row after signing must not affect what was already signed.
            owner.LastName = "Ionescu";
            _dbFixture.Context.SaveChanges();

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("Adela Popescu", record.SignerFullNameSnapshot);
            Assert.Equal(hmacBeforeRename, record.SignatureHmac);
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_ManagerCountersigns_RecordsManagerRole()
        {
            var service = CreateService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);
            var owner = SeedUser("Adela", "Popescu", employeeFunction);
            owner.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SU", "PendingManager");
            doc.UserSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner, doc, "Norme SSM generale", 3m, new DateTime(2026, 2, 1));

            await service.UpdateDocumentSignatureAsync(
                doc.Id, manager.Id, "Manager", "Type", "Radu Stanescu", "9.9.9.9");

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("Manager", record.SignerRole);
            Assert.Equal(manager.Id, record.SignerUserId);
            Assert.Equal("Radu Stanescu", record.SignerFullNameSnapshot);
            Assert.Equal("Sef Echipa", record.SignerPositionSnapshot);
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_SameSignerTwice_SecondRecordChainsToFirst()
        {
            var service = CreateService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);

            var owner1 = SeedUser("Adela", "Popescu", employeeFunction);
            owner1.AssignedToId = manager.Id;
            var owner2 = SeedUser("Ion", "Vasile", employeeFunction);
            owner2.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc1 = SeedDocument(owner1, "SU", "PendingManager");
            doc1.UserSignedAt = DateTime.UtcNow;
            var doc2 = SeedDocument(owner2, "SU", "PendingManager");
            doc2.UserSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();

            // Same manager signs two different employees' documents — the chain is per-signer,
            // not per-document, so the second signature must link to the first regardless.
            await service.UpdateDocumentSignatureAsync(
                doc1.Id, manager.Id, "Manager", "Type", "Radu Stanescu", "9.9.9.9");
            await service.UpdateDocumentSignatureAsync(
                doc2.Id, manager.Id, "Manager", "Type", "Radu Stanescu", "9.9.9.9");

            var firstRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc1.Id);
            var secondRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc2.Id);

            Assert.Null(firstRecord.PreviousSignatureHash);
            Assert.False(string.IsNullOrEmpty(firstRecord.SignatureHmac));
            Assert.Equal(firstRecord.SignatureHmac, secondRecord.PreviousSignatureHash);
            Assert.NotEqual(firstRecord.SignatureHmac, secondRecord.SignatureHmac);
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_SameSignerAcrossLoop_ChainsWithinTheSameBatch()
        {
            var service = CreateService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);

            var owner1 = SeedUser("Adela", "Popescu", employeeFunction);
            owner1.AssignedToId = manager.Id;
            var owner2 = SeedUser("Ion", "Vasile", employeeFunction);
            owner2.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc1 = SeedDocument(owner1, "SU", "PendingManager");
            doc1.UserSignedAt = DateTime.UtcNow;
            var doc2 = SeedDocument(owner2, "SU", "PendingManager");
            doc2.UserSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();

            // A single bulk-sign call processes both documents in one in-memory loop before the
            // caller's own SaveChanges — this proves the chain lookup sees records created
            // earlier in the *same* loop, not just ones from a prior, already-saved request.
            await service.BulkSignDocumentsAsync(false, manager.Id, "Type", "Radu Stanescu", "9.9.9.9");

            var records = _dbFixture.Context.SignatureRecords
                .Where(r => r.SignerUserId == manager.Id)
                .ToList();

            // Both documents share the same SignedAt timestamp (set once for the whole bulk
            // call), so processing order — not the timestamp — is what determines the chain;
            // assert on the link itself rather than assuming which document was processed first.
            Assert.Equal(2, records.Count);
            var withoutPrevious = Assert.Single(records, r => r.PreviousSignatureHash == null);
            var withPrevious = Assert.Single(records, r => r.PreviousSignatureHash != null);
            Assert.Equal(withoutPrevious.SignatureHmac, withPrevious.PreviousSignatureHash);
        }

        [Fact]
        public async Task SignSingleDocumentAsAdminAsync_CreatesSignatureRecordWithAdminRole()
        {
            var service = CreateService();
            var adminFunction = SeedFunction("Inspector SSM");
            var admin = SeedUser("Mihai", "Ionescu", adminFunction, role: UserRole.Admin);
            var employeeFunction = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", employeeFunction);
            var doc = SeedDocument(owner, "SSM", "PendingAdmin");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var loadedDoc = await _dbFixture.Context.UserDocuments
                .Include(d => d.User).ThenInclude(u => u.PeriodicTrainings)
                .Include(d => d.User).ThenInclude(u => u.InitialTrainings)
                .FirstAsync(d => d.Id == doc.Id);

            await service.SignSingleDocumentAsAdminAsync(loadedDoc, admin.Id, "Type", "Mihai Ionescu", "5.6.7.8");

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("Admin", record.SignerRole);
            Assert.Equal(admin.Id, record.SignerUserId);
            Assert.Equal("Mihai Ionescu", record.SignerFullNameSnapshot);
            Assert.Equal("Inspector SSM", record.SignerPositionSnapshot);
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_ManagerBulkSigns_CreatesOneSignatureRecordPerDocument()
        {
            var service = CreateService();
            var employeeFunction = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction);

            var owner1 = SeedUser("Adela", "Popescu", employeeFunction);
            owner1.AssignedToId = manager.Id;
            var owner2 = SeedUser("Ion", "Vasile", employeeFunction);
            owner2.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc1 = SeedDocument(owner1, "SU", "PendingManager");
            doc1.UserSignedAt = DateTime.UtcNow;
            var doc2 = SeedDocument(owner2, "SU", "PendingManager");
            doc2.UserSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();

            var count = await service.BulkSignDocumentsAsync(false, manager.Id, "Type", "Radu Stanescu", "9.9.9.9");

            Assert.Equal(2, count);
            var records = _dbFixture.Context.SignatureRecords.Where(r => r.SignerUserId == manager.Id).ToList();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal("Manager", r.SignerRole));
            Assert.All(records, r => Assert.Equal("Radu Stanescu", r.SignerFullNameSnapshot));
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_SameSlotSignedTwice_BothStampCurrentSchemaVersion()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            // Simulates a revision cycle: the same (training, role) slot is signed, then signed
            // again later (e.g. after the prior signature was invalidated by a content edit).
            // Version records which HMAC canonical schema computed the hash, not which attempt
            // this is — both signatures were made under today's (only) schema, so both get the
            // same Version, and that's correct, not a bug.
            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Draw", "sig-v1", "1.2.3.4");
            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Draw", "sig-v2", "1.2.3.4");

            var versions = _dbFixture.Context.SignatureRecords
                .Where(r => r.UserDocumentId == doc.Id && r.SignerRole == "User")
                .Select(r => r.Version)
                .ToList();

            Assert.Equal(2, versions.Count);
            Assert.All(versions, v => Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, v));
        }

        // ───────────────────────── Manager/Instructor-scoped queues ─────────────────────────

        [Fact]
        public async Task GetManagerPendingSignaturesAsync_ScopesByAssignedManager_NotLinkedInstructor()
        {
            // Manager is resolved purely via AssignedTo — a training row's (unrelated) linked
            // instructor must not affect who sees this in their manager queue.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var assignedManager = SeedUser("Radu", "Stanescu", function);
            var instructor = SeedUser("Elena", "Marin", function);
            var owner = SeedUser("Adela", "Popescu", function);
            owner.AssignedToId = assignedManager.Id;
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SU", "PendingManager");
            doc.UserSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: instructor.Id);

            var forAssignedManager = await service.GetManagerPendingSignaturesAsync(assignedManager.Id);
            var forInstructor = await service.GetManagerPendingSignaturesAsync(instructor.Id);

            Assert.Single(forAssignedManager);
            Assert.Empty(forInstructor);
        }

        [Fact]
        public async Task GetManagerSignedDocumentsAsync_UsesSignatureRecordHistory_NotCurrentInstructorId()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var originalInstructor = SeedUser("Elena", "Marin", function);
            var reassignedInstructor = SeedUser("Ion", "Dobre", function);
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "Completed");
            doc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: reassignedInstructor.Id);

            // The historical fact: originalInstructor actually signed this — even though the row's
            // InstructorId has since been reassigned to someone else.
            _dbFixture.Context.SignatureRecords.Add(new SignatureRecord
            {
                Id = Guid.NewGuid(),
                UserDocumentId = doc.Id,
                PeriodicTrainingId = training.Id,
                SignerRole = "Manager",
                SignerUserId = originalInstructor.Id,
                SignerFullNameSnapshot = "Elena Marin",
                SignerPositionSnapshot = "Operator",
                SignatureData = "sig",
                SignedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            _dbFixture.Context.SaveChanges();

            var forOriginal = await service.GetManagerSignedDocumentsAsync(originalInstructor.Id);
            var forReassigned = await service.GetManagerSignedDocumentsAsync(reassignedInstructor.Id);

            Assert.Single(forOriginal);
            Assert.Empty(forReassigned);
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_ScopesToLinkedInstructor_NotOtherInstructorsDocuments()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var instructorA = SeedUser("Elena", "Marin", function);
            var instructorB = SeedUser("Ion", "Dobre", function);
            var owner1 = SeedUser("Adela", "Popescu", function);
            var owner2 = SeedUser("Vlad", "Georgescu", function);
            _dbFixture.Context.SaveChanges();

            var doc1 = SeedDocument(owner1, "SU", "PendingInstructor");
            doc1.UserSignedAt = DateTime.UtcNow;
            doc1.ManagerSignedAt = DateTime.UtcNow;
            var doc2 = SeedDocument(owner2, "SU", "PendingInstructor");
            doc2.UserSignedAt = DateTime.UtcNow;
            doc2.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner1, doc1, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: instructorA.Id);
            SeedTraining(owner2, doc2, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: instructorB.Id);

            var count = await service.BulkSignDocumentsAsync(false, instructorA.Id, "Type", "Elena Marin", "9.9.9.9");

            Assert.Equal(1, count);
            var record1 = _dbFixture.Context.SignatureRecords.SingleOrDefault(r => r.UserDocumentId == doc1.Id);
            Assert.NotNull(record1);
            Assert.Equal("Instructor", record1!.SignerRole);
            Assert.Null(_dbFixture.Context.SignatureRecords.SingleOrDefault(r => r.UserDocumentId == doc2.Id));
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_CoversBothManagerAndInstructorSteps_ForSameCaller()
        {
            // The same person can be someone's line manager (PendingManager) and someone else's
            // linked instructor (PendingInstructor) at once — a single bulk-sign call covers both.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var person = SeedUser("Radu", "Stanescu", function);
            var managedOwner = SeedUser("Adela", "Popescu", function);
            managedOwner.AssignedToId = person.Id;
            var instructedOwner = SeedUser("Vlad", "Georgescu", function);
            _dbFixture.Context.SaveChanges();

            var managerDoc = SeedDocument(managedOwner, "SU", "PendingManager");
            managerDoc.UserSignedAt = DateTime.UtcNow;
            var instructorDoc = SeedDocument(instructedOwner, "SU", "PendingInstructor");
            instructorDoc.UserSignedAt = DateTime.UtcNow;
            instructorDoc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(instructedOwner, instructorDoc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: person.Id);

            var count = await service.BulkSignDocumentsAsync(false, person.Id, "Type", "Radu Stanescu", "9.9.9.9");

            Assert.Equal(2, count);
            var managerRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == managerDoc.Id);
            var instructorRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == instructorDoc.Id);
            Assert.Equal("Manager", managerRecord.SignerRole);
            Assert.Equal("Instructor", instructorRecord.SignerRole);
        }

        [Fact]
        public void SignatureRecords_SameVersionForSameTrainingAndRole_IsAllowed()
        {
            // Version records an HMAC schema, not a resign ordinal — many SignatureRecords for the
            // same (training, role) slot legitimately share the same Version (they were all signed
            // under today's only schema), so no uniqueness constraint should reject this.
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            _dbFixture.Context.SignatureRecords.Add(NewSignatureRecordRow(doc, training, owner, version: 1));
            _dbFixture.Context.SaveChanges();

            _dbFixture.Context.SignatureRecords.Add(NewSignatureRecordRow(doc, training, owner, version: 1));
            _dbFixture.Context.SaveChanges();

            var count = _dbFixture.Context.SignatureRecords.Count(r => r.PeriodicTrainingId == training.Id);
            Assert.Equal(2, count);
        }

        private static SignatureRecord NewSignatureRecordRow(UserDocument doc, PeriodicTraining training, User signer, int version) => new()
        {
            Id = Guid.NewGuid(),
            UserDocumentId = doc.Id,
            PeriodicTrainingId = training.Id,
            SignerRole = "User",
            SignerUserId = signer.Id,
            SignerFullNameSnapshot = $"{signer.FirstName} {signer.LastName}",
            SignerPositionSnapshot = "Operator",
            SignatureData = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            Version = version
        };

        private SignatureVerificationService CreateVerificationService()
        {
            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            return new SignatureVerificationService(_dbFixture.Context, new HmacSignatureService(keyProviderMock.Object));
        }

        // ───────────────────────── GenerateDocumentAsync regression ─────────────────────────

        [Fact]
        public async Task GenerateDocumentAsync_NewSessionForUserWithSignedHistory_DoesNotDisturbOldSignatureRecord()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc1 = SeedDocument(owner, "SU", "PendingUser");
            var training1 = SeedTraining(owner, doc1, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));

            await service.UpdateDocumentSignatureAsync(doc1.Id, owner.Id, isUserSignature: true, "Draw", "sig-v1", "1.2.3.4");

            var oldRecordBefore = _dbFixture.Context.SignatureRecords
                .AsNoTracking()
                .Single(r => r.UserDocumentId == doc1.Id);

            // Launch a new session: generate a new document of the same type for the same user —
            // this is the real production path (CopyHistoricalPeriodicTrainingRowsAsync +
            // LinkOrCreateCurrentPeriodicTrainingRowAsync), not a hand-simulated approximation.
            var doc2 = await service.GenerateDocumentAsync(owner.Id, "SU", "admin@example.com");

            var oldRecordAfter = _dbFixture.Context.SignatureRecords
                .AsNoTracking()
                .Single(r => r.Id == oldRecordBefore.Id);

            // Byte-identical, not just "still exists" — every field verification depends on must
            // survive generating a new document/session for this user untouched.
            Assert.Equal(oldRecordBefore.SignatureHmac, oldRecordAfter.SignatureHmac);
            Assert.Equal(oldRecordBefore.PreviousSignatureHash, oldRecordAfter.PreviousSignatureHash);
            Assert.Equal(oldRecordBefore.Version, oldRecordAfter.Version);
            Assert.Equal(oldRecordBefore.SignedAt, oldRecordAfter.SignedAt);
            Assert.Equal(oldRecordBefore.SignerFullNameSnapshot, oldRecordAfter.SignerFullNameSnapshot);
            Assert.Equal(oldRecordBefore.SignerPositionSnapshot, oldRecordAfter.SignerPositionSnapshot);
            Assert.Equal(oldRecordBefore.MaterialTaughtSnapshot, oldRecordAfter.MaterialTaughtSnapshot);
            Assert.Equal(oldRecordBefore.DurationHoursSnapshot, oldRecordAfter.DurationHoursSnapshot);
            Assert.Equal(oldRecordBefore.TrainingDateSnapshot, oldRecordAfter.TrainingDateSnapshot);
            Assert.Equal(oldRecordBefore.IsLegacyUnverified, oldRecordAfter.IsLegacyUnverified);

            // The copied historical row on the new document is a display copy only — it must NOT
            // get its own SignatureRecord, or the audit trail would fork. Known behavior, not a bug.
            var copiedRow = _dbFixture.Context.PeriodicTrainings
                .Single(pt => pt.UserDocumentId == doc2.Id && pt.SourceRowId == training1.Id);
            Assert.False(_dbFixture.Context.SignatureRecords.Any(r => r.PeriodicTrainingId == copiedRow.Id));

            var statuses = await CreateVerificationService().GetVerificationStatusForUsersAsync(new[] { owner.Id });
            Assert.Contains(statuses[owner.Id], s => s.SignatureId == oldRecordBefore.Id && s.Status == "Valid");
        }

        [Fact]
        public async Task UpdateDocumentSignatureAsync_NewPeriodicTrainingAddedUnderNewerSchemaVersion_BothSignaturesValidate()
        {
            // Realistic combination: the employee already signed one training session on this
            // document, then a second periodic training session gets added to the SAME document
            // and signed later — after a (simulated) HMAC schema version bump — and both must
            // still validate correctly and independently.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            var training1 = SeedTraining(owner, doc, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));

            await service.UpdateDocumentSignatureAsync(
                documentId: doc.Id,
                signerUserId: owner.Id,
                isUserSignature: true,
                signatureMethod: "Draw",
                signatureData: "sig-v1",
                ipAddress: "1.2.3.4",
                periodicTrainingId: training1.Id);
            var record1 = _dbFixture.Context.SignatureRecords.Single(r => r.PeriodicTrainingId == training1.Id);
            Assert.Equal(SignatureCanonicalSerializer.CurrentVersion, record1.Version);

            // A new instructaj session gets added to the SAME document.
            var training2 = SeedTraining(owner, doc, "Norme SSM v2 - sesiune noua", 3m, new DateTime(2026, 3, 1));

            // Signed later by the SAME employee, simulating a schema version bump having happened
            // in between — the real signing flow always stamps CurrentVersion, so a genuinely
            // different version has to be hand-built here, with a properly computed HMAC (not just
            // a relabeled one), and correctly chained to record1 (same signer, so this record's
            // PreviousSignatureHash must be record1's SignatureHmac, or it would show ChainBroken).
            var record2 = await SeedManuallySignedRecordAsync(doc, training2, "User", owner, "Operator",
                version: 2, previousSignatureHash: record1.SignatureHmac);

            var verificationService = CreateVerificationService();
            var status1 = await verificationService.GetVerificationStatusAsync(record1.Id);
            var status2 = await verificationService.GetVerificationStatusAsync(record2.Id);

            Assert.Equal("Valid", status1!.Status);
            Assert.True(status1.IsChainValid);
            Assert.Equal("Valid", status2!.Status);
            Assert.True(status2.IsChainValid);

            // Both must also show up correctly through the by-users endpoint used in Task 3's flow.
            var statusesByUser = await verificationService.GetVerificationStatusForUsersAsync(new[] { owner.Id });
            Assert.Contains(statusesByUser[owner.Id], s => s.SignatureId == record1.Id && s.Status == "Valid");
            Assert.Contains(statusesByUser[owner.Id], s => s.SignatureId == record2.Id && s.Status == "Valid");
        }

        // Hand-builds a SignatureRecord with a real, correctly-computed HMAC under an explicit
        // schema version — the normal signing flow always stamps
        // SignatureCanonicalSerializer.CurrentVersion, so this is the only way to simulate "a
        // signature made after a hypothetical schema bump" without waiting for a real one to exist.
        private async Task<SignatureRecord> SeedManuallySignedRecordAsync(UserDocument doc, PeriodicTraining training,
            string signerRole, User signer, string position, int version, string? previousSignatureHash = null)
        {
            var signedAt = DateTimeOffset.UtcNow;
            var fullName = $"{signer.FirstName} {signer.LastName}";
            var input = new SignatureCanonicalInput(
                signer.Id,
                fullName,
                position,
                training.MaterialTaught,
                training.DurationHours,
                training.TrainingDate,
                signedAt,
                previousSignatureHash,
                version);

            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            var hmacService = new HmacSignatureService(keyProviderMock.Object);
            var hmac = await hmacService.ComputeHmacAsync(SignatureCanonicalSerializer.Serialize(input));

            var record = new SignatureRecord
            {
                Id = Guid.NewGuid(),
                UserDocumentId = doc.Id,
                PeriodicTrainingId = training.Id,
                SignerRole = signerRole,
                SignerUserId = signer.Id,
                SignerFullNameSnapshot = fullName,
                SignerPositionSnapshot = position,
                SignatureMethod = "Draw",
                SignatureData = "sig-manual",
                MaterialTaughtSnapshot = training.MaterialTaught,
                DurationHoursSnapshot = training.DurationHours,
                TrainingDateSnapshot = training.TrainingDate,
                SignedAt = signedAt,
                PreviousSignatureHash = previousSignatureHash,
                SignatureHmac = hmac,
                IsLegacyUnverified = false,
                Version = version
            };
            _dbFixture.Context.SignatureRecords.Add(record);
            _dbFixture.Context.SaveChanges();
            return record;
        }
    }
}
