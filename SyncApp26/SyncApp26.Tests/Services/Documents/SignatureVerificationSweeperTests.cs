using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Documents
{
    public class SignatureVerificationSweeperTests : IDisposable
    {
        private const string TestKey = "test-signing-key-for-signature-sweep-tests";

        private readonly SqliteContextFixture _dbFixture = new();
        private readonly Mock<ICryptographyService> _cryptographyServiceMock = new();
        private readonly HmacSignatureService _hmacService;

        public SignatureVerificationSweeperTests()
        {
            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            _hmacService = new HmacSignatureService(keyProviderMock.Object);
            _cryptographyServiceMock.Setup(c => c.SignDataAsync(It.IsAny<string>())).ReturnsAsync("rsa-proof");
        }

        public void Dispose() => _dbFixture.Dispose();

        private DocumentService CreateDocumentService() =>
            new(_dbFixture.Context, _cryptographyServiceMock.Object, _hmacService);

        private SignatureVerificationService CreateVerificationService() =>
            new(_dbFixture.Context, _hmacService);

        private SignatureVerificationSweeper CreateSweeper(ISignatureVerificationService? verificationService = null, int pageSize = 500) =>
            new(_dbFixture.Context,
                verificationService ?? CreateVerificationService(),
                Mock.Of<ILogger<SignatureVerificationSweeper>>(),
                pageSize);

        private Function SeedFunction(string name)
        {
            var function = new Function { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Functions.Add(function);
            _dbFixture.Context.SaveChanges();
            return function;
        }

        private User SeedUser(string firstName, string lastName, Function function)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                PersonalId = Guid.NewGuid().ToString(),
                FunctionId = function.Id,
                Role = UserRole.BasicUser,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        private UserDocument SeedDocument(User owner)
        {
            var doc = new UserDocument
            {
                Id = Guid.NewGuid(),
                UserId = owner.Id,
                User = owner,
                DocumentType = "SU",
                Status = "PendingUser",
                GeneratedAt = DateTime.UtcNow,
                DocumentHash = "seed-hash"
            };
            _dbFixture.Context.UserDocuments.Add(doc);
            _dbFixture.Context.SaveChanges();
            return doc;
        }

        // Produces N genuinely-signed SignatureRecords (real HMACs) via the normal signing flow.
        private List<SignatureRecord> SeedSignedRecords(int count)
        {
            var docService = CreateDocumentService();
            var function = SeedFunction($"Operator-{Guid.NewGuid():N}");
            var records = new List<SignatureRecord>();
            for (var i = 0; i < count; i++)
            {
                var owner = SeedUser("Emp", $"Loyee{i}", function);
                var doc = SeedDocument(owner);
                docService.UpdateDocumentSignatureAsync(doc.Id, owner.Id, "User", "Draw", "sig", "1.2.3.4")
                    .GetAwaiter().GetResult();
                records.Add(_dbFixture.Context.SignatureRecords.Single(r => r.UserDocumentId == doc.Id));
            }
            return records;
        }

        [Fact]
        public async Task RunAsync_NoRecords_ReturnsEmptySummary()
        {
            var summary = await CreateSweeper().RunAsync(CancellationToken.None);

            Assert.Equal(0, summary.RecordsChecked);
            Assert.Equal(0, summary.AnomaliesFound);
            Assert.Equal(0, summary.BatchesFailed);
        }

        [Fact]
        public async Task RunAsync_AllValidSignatures_ChecksAllWithNoAnomalies()
        {
            SeedSignedRecords(5);

            var summary = await CreateSweeper().RunAsync(CancellationToken.None);

            Assert.Equal(5, summary.RecordsChecked);
            Assert.Equal(0, summary.AnomaliesFound);
            Assert.Equal(0, summary.BatchesFailed);
        }

        [Fact]
        public async Task RunAsync_TamperedSignature_CountedAsAnomaly()
        {
            var records = SeedSignedRecords(3);
            records[1].SignatureHmac = new string('f', 64);
            _dbFixture.Context.SaveChanges();

            var summary = await CreateSweeper().RunAsync(CancellationToken.None);

            Assert.Equal(3, summary.RecordsChecked);
            Assert.Equal(1, summary.AnomaliesFound);
        }

        [Fact]
        public async Task RunAsync_MoreRecordsThanPageSize_PaginatesAndChecksAll()
        {
            SeedSignedRecords(7);

            // pageSize 2 forces multiple pages (2 + 2 + 2 + 1) — proves the loop advances instead
            // of only ever reading the first page.
            var summary = await CreateSweeper(pageSize: 2).RunAsync(CancellationToken.None);

            Assert.Equal(7, summary.RecordsChecked);
            Assert.Equal(0, summary.BatchesFailed);
        }

        [Fact]
        public async Task RunAsync_OneBatchThrows_ContinuesWithRemainingBatches()
        {
            SeedSignedRecords(4);

            // Mock verification that throws on its first call and succeeds afterwards — the sweep
            // must record the failed batch and keep going, not abort the whole run.
            var verificationMock = new Mock<ISignatureVerificationService>();
            var call = 0;
            verificationMock
                .Setup(s => s.GetVerificationStatusBatchAsync(It.IsAny<IEnumerable<Guid>>()))
                .Returns<IEnumerable<Guid>>(ids =>
                {
                    call++;
                    if (call == 1) throw new InvalidOperationException("simulated batch failure");
                    return Task.FromResult(ids.Select(id => new SignatureVerificationStatusResponseDTO
                    {
                        SignatureId = id,
                        Status = "Valid"
                    }).ToList());
                });

            var summary = await CreateSweeper(verificationMock.Object, pageSize: 2).RunAsync(CancellationToken.None);

            Assert.Equal(1, summary.BatchesFailed);
            // The second page (2 records) was still processed despite the first page throwing.
            Assert.Equal(2, summary.RecordsChecked);
        }

        [Fact]
        public async Task RunAsync_CancellationRequested_StopsWithoutProcessing()
        {
            SeedSignedRecords(3);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var summary = await CreateSweeper().RunAsync(cts.Token);

            Assert.Equal(0, summary.RecordsChecked);
        }
    }
}
