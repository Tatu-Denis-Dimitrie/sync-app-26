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
            var input = new SignatureCanonicalInput(signerUserId, fullName, position, material, duration, trainingDate, signedAt, null);
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

        [Fact]
        public async Task UpdateDocumentSignatureAsync_UserSigns_CreatesSignatureRecordWithFrozenSnapshotAndCorrectHmac()
        {
            var service = CreateService();
            var function = SeedFunction("Operator");
            var owner = SeedUser("Adela", "Popescu", function);
            var doc = SeedDocument(owner, "SU", "PendingUser");
            SeedTraining(owner, doc, "Norme SSM generale", 2m, new DateTime(2026, 1, 15));

            var result = await service.UpdateDocumentSignatureAsync(
                doc.Id, owner.Id, isUserSignature: true, "Draw", "signature-png-data", "1.2.3.4");

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

            await service.UpdateDocumentSignatureAsync(doc.Id, owner.Id, isUserSignature: true, "Draw", "sig", "1.2.3.4");
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
                doc.Id, manager.Id, isUserSignature: false, "Type", "Radu Stanescu", "9.9.9.9", isAdminSignature: false);

            var record = _dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id);
            Assert.Equal("Manager", record.SignerRole);
            Assert.Equal(manager.Id, record.SignerUserId);
            Assert.Equal("Radu Stanescu", record.SignerFullNameSnapshot);
            Assert.Equal("Sef Echipa", record.SignerPositionSnapshot);
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
    }
}
