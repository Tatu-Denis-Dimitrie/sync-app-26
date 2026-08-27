using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.Exceptions;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
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
            string? material, decimal? duration, DateTime? trainingDate, DateTimeOffset signedAt,
            string? badgeNumber = null, string? workSite = null)
        {
            var input = new SignatureCanonicalInput(signerUserId, fullName, position, badgeNumber, workSite, material, duration, trainingDate, signedAt, null, SignatureCanonicalSerializer.CurrentVersion);
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

        private User SeedUser(string firstName, string lastName, Function function, string roleName = Roles.BasicUser,
            string? badgeNumber = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                PersonalId = Guid.NewGuid().ToString(),
                FunctionId = function.Id,
                BadgeNumber = badgeNumber,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            _dbFixture.GrantRole(user, roleName);
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

        // Revising a signed training's content resets UserDocument's Manager* columns so the manager
        // can sign again. Section 3 "Admis la lucru" records a one-time approval, so it must keep
        // showing the manager's FIRST signature and never the columns the re-sign overwrote.
        // (The periodic table is deliberately the opposite — see the per-row test below.)
        [Fact]
        public async Task ManagerReSignAfterTrainingRevision_KeepsFirstSignatureFrozenInAuditRecord()
        {
            var service = CreateService();
            var trainingService = new PeriodicTrainingService(_dbFixture.Context, NullLogger<PeriodicTrainingService>.Instance, RealLocalizerFactory.LocalizationService());

            var function = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction, Roles.LineManager);
            var owner = SeedUser("Adela", "Popescu", function);
            owner.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SSM", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme initiale", 2m, new DateTime(2026, 1, 15), manager.Id);

            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Type", "USER-FIRST", "1.1.1.1", training.Id);
            await service.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Type", "MANAGER-FIRST", "1.1.1.1", training.Id);

            Assert.Equal("MANAGER-FIRST", _dbFixture.Context.UserDocuments.Find(doc.Id)!.ManagerSignatureData);

            // Real revision path: changing signed content clears the document's signature columns.
            await trainingService.UpdateAsync(training.Id, new UpdatePeriodicTrainingDTO
            {
                TrainingDate = new DateTime(2026, 2, 20),
                DurationHours = 4m,
                MaterialTaught = "Norme revizuite",
                InstructorId = manager.Id
            });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.UserDocuments.Find(doc.Id)!.ManagerSignatureData);

            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Type", "USER-SECOND", "1.1.1.1", training.Id);
            await service.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Type", "MANAGER-SECOND", "1.1.1.1", training.Id);

            _dbFixture.Context.ChangeTracker.Clear();

            // The mutable column tracks the newest capture...
            Assert.Equal("MANAGER-SECOND", _dbFixture.Context.UserDocuments.Find(doc.Id)!.ManagerSignatureData);

            // ...while the earliest audit record — the one the PDF renders from — stays on the first.
            // Sorted in memory: SQLite's EF provider can't order by DateTimeOffset server-side,
            // which is why the production lookup does the same.
            var managerRecords = _dbFixture.Context.SignatureRecords
                .Where(r => r.UserDocumentId == doc.Id && r.SignerRole == "Manager")
                .ToList();
            var earliestManagerRecord = managerRecords
                .OrderBy(r => r.SignedAt).ThenBy(r => r.CreatedAt)
                .First();
            Assert.Equal("MANAGER-FIRST", earliestManagerRecord.SignatureData);
            Assert.Equal("Type", earliestManagerRecord.SignatureMethod);

            // Both captures are retained; the render picks the first, not merely the only one.
            Assert.Equal(2, managerRecords.Count);

            // Decisive check that the RENDER is frozen, not just the audit row: rewriting the
            // document's mutable manager column to something wildly different must not change a
            // single byte of the generated PDF. If any render site ever reads that column again,
            // the two documents diverge and this fails.
            async Task<byte[]> RenderAsync()
            {
                _dbFixture.Context.ChangeTracker.Clear();
                var u = await _dbFixture.Context.Users
                    .Include(x => x.Function).Include(x => x.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(x => x.PeriodicTrainings).Include(x => x.InitialTrainings)
                    .FirstAsync(x => x.Id == owner.Id);
                return await service.GeneratePdfBytesAsync(u, _dbFixture.Context.UserDocuments.Find(doc.Id)!);
            }

            var beforeTamper = await RenderAsync();
            Assert.NotEmpty(beforeTamper);

            _dbFixture.Context.ChangeTracker.Clear();
            var tampered = _dbFixture.Context.UserDocuments.Find(doc.Id)!;
            tampered.ManagerSignatureData = "TAMPERED-MANAGER-SIGNATURE-VALUE";
            tampered.ManagerSignatureMethod = "Type";
            _dbFixture.Context.SaveChanges();

            var afterTamper = await RenderAsync();

            // Compared as decompressed page content, not raw bytes: the PDF header carries a
            // CreationDate that changes between generations and would mask the real comparison.
            Assert.Equal(PdfContentStreams(beforeTamper), PdfContentStreams(afterTamper));
        }

        // The periodic-training table logs separate sessions, so each row shows the manager
        // signature captured for THAT row — not the document's first one, the way section 3 does.
        [Fact]
        public async Task PeriodicTrainingRow_ShowsThatRowsOwnManagerSignature_NotTheFirstOne()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction, Roles.LineManager);
            var owner = SeedUser("Adela", "Popescu", function);
            owner.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SSM", "PendingUser");
            var firstSession = SeedTraining(owner, doc, "Sesiunea 1", 2m, new DateTime(2026, 1, 15), manager.Id);
            var secondSession = SeedTraining(owner, doc, "Sesiunea 2", 3m, new DateTime(2026, 6, 20), manager.Id);

            await service.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Type", "SIG-SESSION-ONE", "1.1.1.1", firstSession.Id);
            await service.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Type", "SIG-SESSION-TWO", "1.1.1.1", secondSession.Id);

            async Task<string> RenderAsync()
            {
                _dbFixture.Context.ChangeTracker.Clear();
                var u = await _dbFixture.Context.Users
                    .Include(x => x.Function).Include(x => x.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(x => x.PeriodicTrainings).Include(x => x.InitialTrainings)
                    .FirstAsync(x => x.Id == owner.Id);
                return PdfContentStreams(await service.GeneratePdfBytesAsync(u, _dbFixture.Context.UserDocuments.Find(doc.Id)!));
            }

            var before = await RenderAsync();

            // Rewrite ONLY the second session's own record. If each row rendered the document's
            // first signature, this row would be reading session one's record and nothing would move.
            _dbFixture.Context.ChangeTracker.Clear();
            var secondRecord = _dbFixture.Context.SignatureRecords
                .Single(r => r.PeriodicTrainingId == secondSession.Id && r.SignerRole == "Manager");
            secondRecord.SignatureData = "SIG-SESSION-TWO-EDITED-TO-A-VERY-DIFFERENT-VALUE";
            _dbFixture.Context.SaveChanges();

            var after = await RenderAsync();

            Assert.NotEqual(before, after);
        }

        // "3. Admis la lucru" prints a "Data:" line. When nobody filled in User.AdmittedDate it used
        // to render as a blank rule even though the signature block right below already showed when
        // the manager signed; it now falls back to that same date.
        [Fact]
        public async Task AdmittedToWorkDate_WhenNotExplicitlySet_FallsBackToManagerSigningDate()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var managerFunction = SeedFunction("Sef Echipa");
            var manager = SeedUser("Radu", "Stanescu", managerFunction, Roles.LineManager);
            var owner = SeedUser("Adela", "Popescu", function);
            owner.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SSM", "PendingUser");
            var training = SeedTraining(owner, doc, "Norme initiale", 2m, new DateTime(2026, 1, 15), manager.Id);

            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Type", "USER-SIG", "1.1.1.1", training.Id);
            await service.UpdateDocumentSignatureAsync(doc.Id, manager.Id, "Manager", "Type", "MANAGER-SIG", "1.1.1.1", training.Id);

            async Task<string> RenderAsync()
            {
                _dbFixture.Context.ChangeTracker.Clear();
                var u = await _dbFixture.Context.Users
                    .Include(x => x.Function).Include(x => x.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(x => x.PeriodicTrainings).Include(x => x.InitialTrainings)
                    .FirstAsync(x => x.Id == owner.Id);
                var pdf = await service.GeneratePdfBytesAsync(u, _dbFixture.Context.UserDocuments.Find(doc.Id)!);
                return PdfContentStreams(pdf);
            }

            // AdmittedDate is still null here — the line must already carry the signing date.
            Assert.Null(_dbFixture.Context.Users.Find(owner.Id)!.AdmittedDate);
            var withFallback = await RenderAsync();

            // Setting AdmittedDate explicitly to that same day must render identically, which is only
            // true if the blank case resolved to the manager's signing date rather than an empty rule.
            var managerSignedOn = _dbFixture.Context.SignatureRecords
                .Where(r => r.UserDocumentId == doc.Id && r.SignerRole == "Manager")
                .ToList()
                .OrderBy(r => r.SignedAt).ThenBy(r => r.CreatedAt)
                .First().SignedAt.UtcDateTime.Date;

            _dbFixture.Context.ChangeTracker.Clear();
            var toUpdate = _dbFixture.Context.Users.Find(owner.Id)!;
            toUpdate.AdmittedDate = managerSignedOn;
            _dbFixture.Context.SaveChanges();

            var withExplicitDate = await RenderAsync();

            Assert.Equal(withExplicitDate, withFallback);
        }

        // Concatenates every decompressed stream in a PDF — the drawing operators actually rendered,
        // free of the timestamped file header.
        private static string PdfContentStreams(byte[] pdf)
        {
            var raw = Encoding.Latin1.GetString(pdf);
            var sb = new StringBuilder();
            int pos = 0;
            while (true)
            {
                int start = raw.IndexOf("stream", pos, StringComparison.Ordinal);
                if (start < 0) break;
                int dataStart = start + "stream".Length;
                while (dataStart < raw.Length && (raw[dataStart] == '\r' || raw[dataStart] == '\n')) dataStart++;
                int end = raw.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (end < 0) break;

                var chunk = Encoding.Latin1.GetBytes(raw.Substring(dataStart, end - dataStart));
                try
                {
                    using var input = new MemoryStream(chunk);
                    using var inflate = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    inflate.CopyTo(output);
                    sb.Append(Encoding.Latin1.GetString(output.ToArray()));
                }
                catch { /* not a flate stream (embedded font or image) — nothing to compare */ }

                pos = end + "endstream".Length;
            }
            return sb.ToString();
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
        public async Task UpdateDocumentSignatureAsync_SignerHasBadgeNumber_FreezesItAndBindsItIntoTheHmac()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function, badgeNumber: "BADGE-4471");
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            await service.UpdateDocumentSignatureAsync(
                doc.Id, owner.Id, "User", "Draw", "signature-png-data", "1.2.3.4");

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("BADGE-4471", record.SignerBadgeNumberSnapshot);

            var expected = ExpectedHmac(owner.Id, "Adela Popescu", "Operator", "Norme SSM generale", 2m,
                new DateTime(2026, 1, 15), record.SignedAt, badgeNumber: "BADGE-4471");
            Assert.Equal(expected, record.SignatureHmac);

            // The badge is genuinely part of the hashed input, not just stored alongside it.
            var withoutBadge = ExpectedHmac(owner.Id, "Adela Popescu", "Operator", "Norme SSM generale", 2m,
                new DateTime(2026, 1, 15), record.SignedAt);
            Assert.NotEqual(withoutBadge, record.SignatureHmac);
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
            await service.BulkSignDocumentsAsync(manager.Id, "Type", "Radu Stanescu", "9.9.9.9");

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
        public async Task SignSingleDocumentAsOfficerAsync_CreatesSignatureRecordWithInstructorRole()
        {
            var service = CreateService();
            var officerFunction = SeedFunction("Inspector SSM");
            var officer = SeedUser("Mihai", "Ionescu", officerFunction, roleName: Roles.SsmOfficer);
            var employeeFunction = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", employeeFunction);
            var doc = SeedDocument(owner, "SSM", "PendingInstructor");
            doc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var loadedDoc = await _dbFixture.Context.UserDocuments
                .Include(d => d.User).ThenInclude(u => u.PeriodicTrainings)
                .Include(d => d.User).ThenInclude(u => u.InitialTrainings)
                .FirstAsync(d => d.Id == doc.Id);

            await service.SignSingleDocumentAsOfficerAsync(loadedDoc, officer.Id, "Type", "Mihai Ionescu", "5.6.7.8");

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("Instructor", record.SignerRole);
            Assert.Equal(officer.Id, record.SignerUserId);
            Assert.Equal("Mihai Ionescu", record.SignerFullNameSnapshot);
            Assert.Equal("Inspector SSM", record.SignerPositionSnapshot);
            Assert.Equal("Completed", loadedDoc.Status);
        }

        [Fact]
        public async Task SignSingleDocumentAsOfficerAsync_SignerLacksOfficerRoleForType_Throws()
        {
            var service = CreateService();
            var officerFunction = SeedFunction("Inspector SU");
            // Holds the SU officer role, not SSM — must not be able to sign an SSM document.
            var wrongTypeOfficer = SeedUser("Mihai", "Ionescu", officerFunction, roleName: Roles.SuOfficer);
            var employeeFunction = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", employeeFunction);
            var doc = SeedDocument(owner, "SSM", "PendingInstructor");
            doc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();

            var loadedDoc = await _dbFixture.Context.UserDocuments
                .Include(d => d.User).ThenInclude(u => u.PeriodicTrainings)
                .Include(d => d.User).ThenInclude(u => u.InitialTrainings)
                .FirstAsync(d => d.Id == doc.Id);

            await Assert.ThrowsAsync<DocumentSigningAuthorizationException>(() =>
                service.SignSingleDocumentAsOfficerAsync(loadedDoc, wrongTypeOfficer.Id, "Type", "Mihai Ionescu", "5.6.7.8"));
        }

        [Fact]
        public async Task SignSingleDocumentAsOfficerAsync_DocumentNotPendingInstructor_Throws()
        {
            var service = CreateService();
            var officerFunction = SeedFunction("Inspector SSM");
            var officer = SeedUser("Mihai", "Ionescu", officerFunction, roleName: Roles.SsmOfficer);
            var employeeFunction = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", employeeFunction);
            // Still waiting on the manager — not yet this officer's turn to sign.
            var doc = SeedDocument(owner, "SSM", "PendingManager");
            _dbFixture.Context.SaveChanges();

            var loadedDoc = await _dbFixture.Context.UserDocuments
                .Include(d => d.User).ThenInclude(u => u.PeriodicTrainings)
                .Include(d => d.User).ThenInclude(u => u.InitialTrainings)
                .FirstAsync(d => d.Id == doc.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SignSingleDocumentAsOfficerAsync(loadedDoc, officer.Id, "Type", "Mihai Ionescu", "5.6.7.8"));
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

            var count = await service.BulkSignDocumentsAsync(manager.Id, "Type", "Radu Stanescu", "9.9.9.9");

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

            var forAssignedManager = await service.GetManagerPendingSignaturesAsync(assignedManager.Id, 1, 50);
            var forInstructor = await service.GetManagerPendingSignaturesAsync(instructor.Id, 1, 50);

            Assert.Single(forAssignedManager.Items);
            Assert.Empty(forInstructor.Items);
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

            var forOriginal = await service.GetManagerSignedDocumentsAsync(originalInstructor.Id, 1, 50);
            var forReassigned = await service.GetManagerSignedDocumentsAsync(reassignedInstructor.Id, 1, 50);

            Assert.Single(forOriginal.Items);
            Assert.Empty(forReassigned.Items);
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_ScopesToOfficerRole_NotDocumentTypeMismatch()
        {
            // The officer role — not any per-row InstructorId link — decides eligibility: an SsmOfficer
            // signs every pending SSM document regardless of who was ever linked as instructor, and
            // never touches SU documents even if pending.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var ssmOfficer = SeedUser("Elena", "Marin", function, roleName: Roles.SsmOfficer);
            var owner1 = SeedUser("Adela", "Popescu", function);
            var owner2 = SeedUser("Vlad", "Georgescu", function);
            _dbFixture.Context.SaveChanges();

            var ssmDoc = SeedDocument(owner1, "SSM", "PendingInstructor");
            ssmDoc.UserSignedAt = DateTime.UtcNow;
            ssmDoc.ManagerSignedAt = DateTime.UtcNow;
            var suDoc = SeedDocument(owner2, "SU", "PendingInstructor");
            suDoc.UserSignedAt = DateTime.UtcNow;
            suDoc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner1, ssmDoc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));
            SeedTraining(owner2, suDoc, "Norme SU generale", 2m, new DateTime(2026, 1, 15));

            var count = await service.BulkSignDocumentsAsync(ssmOfficer.Id, "Type", "Elena Marin", "9.9.9.9");

            Assert.Equal(1, count);
            var ssmRecord = _dbFixture.Context.SignatureRecords.SingleOrDefault(r => r.UserDocumentId == ssmDoc.Id);
            Assert.NotNull(ssmRecord);
            Assert.Equal("Instructor", ssmRecord.SignerRole);
            Assert.Null(_dbFixture.Context.SignatureRecords.SingleOrDefault(r => r.UserDocumentId == suDoc.Id));
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_NonOfficer_SignsNoInstructorDocuments()
        {
            // Being linked as InstructorId on a training row no longer grants signing eligibility —
            // only holding the SsmOfficer/SuOfficer role does.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var linkedButNotOfficer = SeedUser("Ion", "Dobre", function);
            var owner = SeedUser("Adela", "Popescu", function);
            _dbFixture.Context.SaveChanges();

            var doc = SeedDocument(owner, "SU", "PendingInstructor");
            doc.UserSignedAt = DateTime.UtcNow;
            doc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(owner, doc, "Norme SU generale", 2m, new DateTime(2026, 1, 15), instructorId: linkedButNotOfficer.Id);

            var count = await service.BulkSignDocumentsAsync(linkedButNotOfficer.Id, "Type", "Ion Dobre", "9.9.9.9");

            Assert.Equal(0, count);
        }

        [Fact]
        public async Task BulkSignDocumentsAsync_CoversBothManagerAndInstructorSteps_ForSameCaller()
        {
            // The same person can be someone's line manager (PendingManager, via AssignedTo) and,
            // separately, hold the SuOfficer role (every SU employee's PendingInstructor) at once —
            // a single bulk-sign call covers both.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var person = SeedUser("Radu", "Stanescu", function, roleName: Roles.SuOfficer);
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
            SeedTraining(instructedOwner, instructorDoc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var count = await service.BulkSignDocumentsAsync(person.Id, "Type", "Radu Stanescu", "9.9.9.9");

            Assert.Equal(2, count);
            var managerRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == managerDoc.Id);
            var instructorRecord = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == instructorDoc.Id);
            Assert.Equal("Manager", managerRecord.SignerRole);
            Assert.Equal("Instructor", instructorRecord.SignerRole);
        }

        // ───────────────────────── Separation of duties ─────────────────────────

        [Fact]
        public async Task BulkSignDocumentsAsync_SkipsTheSignersOwnDocument_ButSignsColleagues()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SuOfficer);
            var colleague = SeedUser("Adela", "Popescu", function);

            // The officer is also an employee with an SU document of their own, at the same step.
            var ownDoc = SeedDocument(officer, "SU", "PendingInstructor");
            ownDoc.UserSignedAt = DateTime.UtcNow;
            ownDoc.ManagerSignedAt = DateTime.UtcNow;
            var colleagueDoc = SeedDocument(colleague, "SU", "PendingInstructor");
            colleagueDoc.UserSignedAt = DateTime.UtcNow;
            colleagueDoc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            SeedTraining(colleague, colleagueDoc, "Norme SU", 2m, new DateTime(2026, 1, 15));
            SeedTraining(officer, ownDoc, "Norme SU", 2m, new DateTime(2026, 1, 15));

            var count = await service.BulkSignDocumentsAsync(officer.Id, "Type", "Radu Stanescu", "9.9.9.9");

            Assert.Equal(1, count);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(DocumentStatuses.Completed, _dbFixture.Context.UserDocuments.Find(colleagueDoc.Id)!.Status);
            Assert.Equal(DocumentStatuses.PendingInstructor, _dbFixture.Context.UserDocuments.Find(ownDoc.Id)!.Status);
            Assert.Empty(_dbFixture.Context.SignatureRecords.Where(r => r.UserDocumentId == ownDoc.Id));
        }

        [Fact]
        public async Task OfficerQueue_CountAndList_BothExcludeTheOfficersOwnDocument()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SsmOfficer);
            var colleague = SeedUser("Adela", "Popescu", function);

            var ownDoc = SeedDocument(officer, "SSM", "PendingInstructor");
            var colleagueDoc = SeedDocument(colleague, "SSM", "PendingInstructor");
            _dbFixture.Context.SaveChanges();

            var count = await service.GetPendingDocumentsForOfficerAsync("SSM", officer.Id);
            var list = await service.GetPendingDocumentsForOfficerListAsync("SSM", officer.Id);

            // Count and list must agree, or the bulk-sign job's progress total never completes.
            Assert.Equal(1, count);
            Assert.Equal(count, list.Count);
            Assert.Equal(colleagueDoc.Id, Assert.Single(list).Id);
            Assert.DoesNotContain(list, d => d.Id == ownDoc.Id);
        }

        [Fact]
        public async Task InstructorPendingQueue_ExcludesTheOfficersOwnDocument()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SsmOfficer);
            var colleague = SeedUser("Adela", "Popescu", function);

            var ownDoc = SeedDocument(officer, "SSM", "PendingInstructor");
            ownDoc.ManagerSignedAt = DateTime.UtcNow;
            var colleagueDoc = SeedDocument(colleague, "SSM", "PendingInstructor");
            colleagueDoc.ManagerSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();

            var pending = await service.GetInstructorPendingSignaturesAsync(officer.Id, 1, 50);

            Assert.Equal(colleagueDoc.Id, Assert.Single(pending.Items).Id);
            Assert.DoesNotContain(pending.Items, d => d.Id == ownDoc.Id);
        }

        [Fact]
        public async Task GetInstructorSignedDocumentsAsync_UsesSignatureRecordHistory_NotCurrentInstructorId()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var originalInstructor = SeedUser("Elena", "Marin", function, roleName: Roles.SsmOfficer);
            var reassignedInstructor = SeedUser("Ion", "Dobre", function, roleName: Roles.SsmOfficer);
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SSM", "Completed");
            doc.InstructorSignedAt = DateTime.UtcNow;
            _dbFixture.Context.SaveChanges();
            var training = SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15), instructorId: reassignedInstructor.Id);

            _dbFixture.Context.SignatureRecords.Add(new SignatureRecord
            {
                Id = Guid.NewGuid(),
                UserDocumentId = doc.Id,
                PeriodicTrainingId = training.Id,
                SignerRole = "Instructor",
                SignerUserId = originalInstructor.Id,
                SignerFullNameSnapshot = "Elena Marin",
                SignerPositionSnapshot = "Operator",
                SignatureData = "sig",
                SignedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
            _dbFixture.Context.SaveChanges();

            var forOriginal = await service.GetInstructorSignedDocumentsAsync(originalInstructor.Id, 1, 50);
            var forReassigned = await service.GetInstructorSignedDocumentsAsync(reassignedInstructor.Id, 1, 50);

            Assert.Single(forOriginal.Items);
            Assert.Empty(forReassigned.Items);
        }

        // ───────────────────────── Pagination correctness ─────────────────────────
        // The one class of bug this whole region guards against: counting after Take instead of
        // before it (TotalCount silently equal to the page size, not the real total), or getting
        // the Skip/Take math off by one. Every paginated method gets one test walking real pages
        // against a small seeded set, so the assertion is decisive rather than "some items came back".

        [Fact]
        public async Task GetMyPendingSignaturesPageAsync_FiltersToPendingUserStatus()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var pending = SeedDocument(owner, "SSM", DocumentStatuses.PendingUser);
            SeedDocument(owner, "SU", DocumentStatuses.Completed);

            var (items, totalCount) = await service.GetMyPendingSignaturesPageAsync(owner.Id, 1, 10);

            Assert.Equal(pending.Id, Assert.Single(items).Id);
            Assert.Equal(1, totalCount);
        }

        [Fact]
        public async Task GetMySignedDocumentsPageAsync_FiltersToUserSignedNotNull()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var signed = SeedDocument(owner, "SSM", DocumentStatuses.PendingManager);
            signed.UserSignedAt = DateTime.UtcNow;
            SeedDocument(owner, "SU", DocumentStatuses.PendingUser);
            _dbFixture.Context.SaveChanges();

            var (items, totalCount) = await service.GetMySignedDocumentsPageAsync(owner.Id, 1, 10);

            Assert.Equal(signed.Id, Assert.Single(items).Id);
            Assert.Equal(1, totalCount);
        }

        // Five documents with distinct GeneratedAt, requesting the middle page — proves Skip/Take
        // is applied correctly AND that TotalCount reflects all 5, not just the page of 2.
        [Fact]
        public async Task GetMyPendingSignaturesPageAsync_RespectsSkipAndTake()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var docs = SeedGeneratedAtSequence(owner, "SSM", DocumentStatuses.PendingUser, 5);

            var (items, totalCount) = await service.GetMyPendingSignaturesPageAsync(owner.Id, 2, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(new[] { docs[2].Id, docs[1].Id }, items.Select(d => d.Id));
        }

        [Fact]
        public async Task GetMyPendingSignaturesPageAsync_LastPageReturnsRemainder()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var docs = SeedGeneratedAtSequence(owner, "SSM", DocumentStatuses.PendingUser, 5);

            var (items, totalCount) = await service.GetMyPendingSignaturesPageAsync(owner.Id, 3, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(docs[0].Id, Assert.Single(items).Id);
        }

        [Fact]
        public async Task GetManagerPendingSignaturesAsync_RespectsSkipAndTake()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var manager = SeedUser("Radu", "Stanescu", function);
            var owner = SeedUser("Adela", "Popescu", function);
            owner.AssignedToId = manager.Id;
            _dbFixture.Context.SaveChanges();
            var docs = SeedGeneratedAtSequence(owner, "SSM", DocumentStatuses.PendingManager, 5,
                d => d.UserSignedAt = DateTime.UtcNow);

            var (items, totalCount) = await service.GetManagerPendingSignaturesAsync(manager.Id, 2, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(new[] { docs[2].Id, docs[1].Id }, items.Select(d => d.Id));
        }

        [Fact]
        public async Task GetManagerSignedDocumentsAsync_RespectsSkipAndTake()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var manager = SeedUser("Radu", "Stanescu", function);
            var owner = SeedUser("Adela", "Popescu", function);
            var docs = SeedGeneratedAtSequence(owner, "SU", DocumentStatuses.Completed, 5);
            foreach (var doc in docs)
            {
                _dbFixture.Context.SignatureRecords.Add(new SignatureRecord
                {
                    Id = Guid.NewGuid(),
                    UserDocumentId = doc.Id,
                    SignerRole = "Manager",
                    SignerUserId = manager.Id,
                    SignerFullNameSnapshot = "Radu Stanescu",
                    SignerPositionSnapshot = "Operator",
                    SignatureData = "sig",
                    SignedAt = DateTimeOffset.UtcNow,
                    Version = 1
                });
            }
            _dbFixture.Context.SaveChanges();

            var (items, totalCount) = await service.GetManagerSignedDocumentsAsync(manager.Id, 2, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(new[] { docs[2].Id, docs[1].Id }, items.Select(d => d.Id));
        }

        [Fact]
        public async Task GetInstructorPendingSignaturesAsync_RespectsSkipAndTake()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SsmOfficer);
            var owner = SeedUser("Adela", "Popescu", function);
            var docs = SeedGeneratedAtSequence(owner, "SSM", DocumentStatuses.PendingInstructor, 5,
                d => d.ManagerSignedAt = DateTime.UtcNow);

            var (items, totalCount) = await service.GetInstructorPendingSignaturesAsync(officer.Id, 2, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(new[] { docs[2].Id, docs[1].Id }, items.Select(d => d.Id));
        }

        [Fact]
        public async Task GetInstructorSignedDocumentsAsync_RespectsSkipAndTake()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SsmOfficer);
            var owner = SeedUser("Adela", "Popescu", function);
            var docs = SeedGeneratedAtSequence(owner, "SSM", DocumentStatuses.Completed, 5);
            foreach (var doc in docs)
            {
                _dbFixture.Context.SignatureRecords.Add(new SignatureRecord
                {
                    Id = Guid.NewGuid(),
                    UserDocumentId = doc.Id,
                    SignerRole = "Instructor",
                    SignerUserId = officer.Id,
                    SignerFullNameSnapshot = "Radu Stanescu",
                    SignerPositionSnapshot = "Operator",
                    SignatureData = "sig",
                    SignedAt = DateTimeOffset.UtcNow,
                    Version = 1
                });
            }
            _dbFixture.Context.SaveChanges();

            var (items, totalCount) = await service.GetInstructorSignedDocumentsAsync(officer.Id, 2, 2);

            Assert.Equal(5, totalCount);
            Assert.Equal(new[] { docs[2].Id, docs[1].Id }, items.Select(d => d.Id));
        }

        // The Include-trim regression: the 5 original methods included both InitialTrainings and
        // PeriodicTrainings (two sibling collections under User) alongside Skip/Take on the root
        // query — a known EF Core cartesian-multiplication hazard. This seeds a user with 2+ rows in
        // BOTH collections at once and proves TotalCount/Items.Count are still correct, against real
        // SQLite (not a mock) — exactly where a cartesian blow-up would actually surface.
        [Fact]
        public async Task GetMyPendingSignaturesPageAsync_MultipleSiblingCollections_DoesNotMultiplyRows()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc1 = SeedDocument(owner, "SSM", DocumentStatuses.PendingUser);
            var doc2 = SeedDocument(owner, "SU", DocumentStatuses.PendingUser);

            SeedTraining(owner, doc1, "Norme SSM 1", 2m, new DateTime(2026, 1, 15));
            SeedTraining(owner, doc1, "Norme SSM 2", 2m, new DateTime(2026, 2, 15));
            _dbFixture.Context.UserInitialTrainings.Add(new UserInitialTraining
            { Id = Guid.NewGuid(), UserId = owner.Id, DocumentType = "SSM", CreatedAt = DateTime.UtcNow });
            _dbFixture.Context.UserInitialTrainings.Add(new UserInitialTraining
            { Id = Guid.NewGuid(), UserId = owner.Id, DocumentType = "SU", CreatedAt = DateTime.UtcNow });
            _dbFixture.Context.SaveChanges();

            var (items, totalCount) = await service.GetMyPendingSignaturesPageAsync(owner.Id, 1, 10);

            Assert.Equal(2, totalCount);
            Assert.Equal(2, items.Count);
            Assert.Equal(new[] { doc1.Id, doc2.Id }.OrderBy(id => id), items.Select(d => d.Id).OrderBy(id => id));
        }

        // Seeds `count` documents for `owner` with strictly increasing GeneratedAt (oldest first, so
        // docs[^1] is newest) — every paginated method orders GeneratedAt descending, so this gives
        // deterministic, distinct page contents to assert against.
        private List<UserDocument> SeedGeneratedAtSequence(User owner, string documentType, string status, int count, Action<UserDocument>? mutate = null)
        {
            var docs = new List<UserDocument>();
            var baseTime = DateTime.UtcNow.AddDays(-count);
            for (int i = 0; i < count; i++)
            {
                var doc = SeedDocument(owner, documentType, status);
                doc.GeneratedAt = baseTime.AddMinutes(i);
                mutate?.Invoke(doc);
                docs.Add(doc);
            }
            _dbFixture.Context.SaveChanges();
            return docs;
        }

        [Fact]
        public async Task SignSingleDocumentAsOfficerAsync_OwnDocument_ThrowsEvenThoughOfficerRoleIsHeld()
        {
            // Defence in depth: the queues already filter this out, so reaching here means a query
            // was loosened — refuse rather than write a signature the rule forbids.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var officer = SeedUser("Radu", "Stanescu", function, roleName: Roles.SsmOfficer);
            var ownDoc = SeedDocument(officer, "SSM", "PendingInstructor");
            _dbFixture.Context.SaveChanges();

            await Assert.ThrowsAsync<DocumentSigningAuthorizationException>(() =>
                service.SignSingleDocumentAsOfficerAsync(ownDoc, officer.Id, "Type", "Radu Stanescu", "9.9.9.9"));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(DocumentStatuses.PendingInstructor, _dbFixture.Context.UserDocuments.Find(ownDoc.Id)!.Status);
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

            await service.UpdateDocumentSignatureAsync(doc1.Id, owner.Id, "User", "Draw", "sig-v1", "1.2.3.4");

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
        public async Task GenerateDocumentAsync_SourceRowExcludedFromPrint_CopiedHistoricalRowInheritsExclusion()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var admin = SeedUser("Admin", "User", function);
            var owner = SeedUser("Adela", "Popescu", function);
            var doc1 = SeedDocument(owner, "SU", "PendingUser");
            var training1 = SeedTraining(owner, doc1, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));

            await service.UpdateDocumentSignatureAsync(doc1.Id, owner.Id, "User", "Draw", "sig-v1", "1.2.3.4");

            training1.ExcludedFromPrintAt = DateTime.UtcNow;
            training1.ExcludedFromPrintById = admin.Id;
            await _dbFixture.Context.SaveChangesAsync();

            var doc2 = await service.GenerateDocumentAsync(owner.Id, "SU", "admin@example.com");

            var copiedRow = _dbFixture.Context.PeriodicTrainings
                .Single(pt => pt.UserDocumentId == doc2.Id && pt.SourceRowId == training1.Id);
            Assert.NotNull(copiedRow.ExcludedFromPrintAt);
            Assert.Equal(admin.Id, copiedRow.ExcludedFromPrintById);
        }

        [Fact]
        public async Task PeriodicTrainingRow_CopiedIntoNewerDocument_StillShowsOriginalSigningDate()
        {
            // CopyHistoricalPeriodicTrainingRowsAsync gives the copy a brand-new Id (SourceRowId
            // points back to the original) - the render must resolve signatures through that chain,
            // or every row carried into a new document (i.e. most rows on any multi-session
            // document) renders with a missing "-" signing date instead of the real one.
            //
            // Two sessions on doc1, both signed as "User": session one seeds
            // ctx.InitialTrainingSignatures (earliest-record-wins, feeds the cover page) which is
            // already cross-document and would mask this bug if tampered. Session two is what this
            // test tampers and checks, isolating the periodic-table-row lookup this fix touches.
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc1 = SeedDocument(owner, "SU", "PendingUser");
            var session1 = SeedTraining(owner, doc1, "Norme SSM v1", 2m, new DateTime(2026, 1, 15));
            var session2 = SeedTraining(owner, doc1, "Norme SSM v2", 2m, new DateTime(2026, 3, 10));

            await service.UpdateDocumentSignatureAsync(doc1.Id, owner.Id, "User", "Draw", "sig-session-one", "1.2.3.4", session1.Id);
            await service.UpdateDocumentSignatureAsync(doc1.Id, owner.Id, "User", "Draw", "sig-session-two", "1.2.3.4", session2.Id);

            var doc2 = await service.GenerateDocumentAsync(owner.Id, "SU", "admin@example.com");
            var copiedRow2 = _dbFixture.Context.PeriodicTrainings
                .Single(pt => pt.UserDocumentId == doc2.Id && pt.SourceRowId == session2.Id);
            Assert.NotEqual(session2.Id, copiedRow2.Id);

            async Task<string> RenderDoc2Async()
            {
                _dbFixture.Context.ChangeTracker.Clear();
                var u = await _dbFixture.Context.Users
                    .Include(x => x.Function).Include(x => x.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(x => x.PeriodicTrainings).Include(x => x.InitialTrainings)
                    .FirstAsync(x => x.Id == owner.Id);
                return PdfContentStreams(await service.GeneratePdfBytesAsync(u, _dbFixture.Context.UserDocuments.Find(doc2.Id)!));
            }

            var before = await RenderDoc2Async();

            // The only SignatureRecord for session two's signature is keyed to session2.Id (the
            // ORIGINAL row), never to copiedRow2.Id. Moving its date must move copiedRow2's rendered
            // date too, if the lookup resolves through SourceRowId - if it were keyed on
            // copiedRow2.Id instead, this edit would have no effect on doc2's render.
            _dbFixture.Context.ChangeTracker.Clear();
            var session2Record = _dbFixture.Context.SignatureRecords
                .Single(r => r.PeriodicTrainingId == session2.Id && r.SignerRole == "User");
            session2Record.SignedAt = session2Record.SignedAt.AddDays(30);
            _dbFixture.Context.SaveChanges();

            var after = await RenderDoc2Async();

            Assert.NotEqual(before, after);
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
                signerRole: "User",
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
                signer.BadgeNumber,
                signer.WorkSite?.Name,
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
                SignerBadgeNumberSnapshot = signer.BadgeNumber,
                SignerWorkSiteNameSnapshot = signer.WorkSite?.Name,
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
