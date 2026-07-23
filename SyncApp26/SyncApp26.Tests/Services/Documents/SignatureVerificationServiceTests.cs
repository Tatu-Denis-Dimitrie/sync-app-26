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

        private SignatureRecord SignDocument(DocumentService docService, UserDocument doc, User signer, bool isUserSignature = true)
        {
            docService.UpdateDocumentSignatureAsync(doc.Id, signer.Id, isUserSignature, "Draw", "sig-data", "1.2.3.4")
                .GetAwaiter().GetResult();
            return _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
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

            SignDocument(docService, doc1, manager, isUserSignature: false);
            var secondRecord = SignDocument(docService, doc2, manager, isUserSignature: false);

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

            SignDocument(docService, doc1, manager, isUserSignature: false);
            var secondRecord = SignDocument(docService, doc2, manager, isUserSignature: false);

            // Simulate an attacker who knows the signing key: relink the record to a
            // nonexistent predecessor and recompute a self-consistent HMAC over the forgery.
            // The hash alone can't catch this — only the chain-continuity check can.
            var forgedPreviousHash = new string('a', 64);
            var forgedInput = new SignatureCanonicalInput(
                secondRecord.SignerUserId,
                secondRecord.SignerFullNameSnapshot,
                secondRecord.SignerPositionSnapshot,
                secondRecord.MaterialTaughtSnapshot,
                secondRecord.DurationHoursSnapshot,
                secondRecord.TrainingDateSnapshot,
                secondRecord.SignedAt,
                forgedPreviousHash);
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
    }
}
