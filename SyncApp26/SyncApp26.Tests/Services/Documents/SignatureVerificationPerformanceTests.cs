using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Tests.TestHelpers;
using Xunit.Abstractions;

namespace SyncApp26.Tests.Services.Documents
{
    /// <summary>
    /// Measures the cost of signature verification at scale to inform whether the periodic sweep
    /// is cheap enough to enable in production. Reports timings via test output rather
    /// than asserting tight per-op thresholds — those would flake on different hardware. Only a
    /// loose correctness/sanity bound is asserted (everything got checked, nothing pathologically
    /// slow). Runs at 1,000 and 5,000 records as part of the normal suite; set the
    /// SIGNATURE_PERF_COUNT environment variable to also measure a larger volume on demand, e.g.
    /// `SIGNATURE_PERF_COUNT=50000 dotnet test --filter FullyQualifiedName~Performance`.
    /// </summary>
    public class SignatureVerificationPerformanceTests : IDisposable
    {
        private const string TestKey = "test-signing-key-for-signature-performance-tests";

        private readonly SqliteContextFixture _dbFixture = new();
        private readonly HmacSignatureService _hmacService;
        private readonly ITestOutputHelper _output;

        public SignatureVerificationPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            var keyProviderMock = new Mock<ISignatureKeyProvider>();
            keyProviderMock.Setup(p => p.GetCurrentKeyAsync()).ReturnsAsync(Encoding.UTF8.GetBytes(TestKey));
            _hmacService = new HmacSignatureService(keyProviderMock.Object);
        }

        public void Dispose() => _dbFixture.Dispose();

        // Always exercises 1,000 and 5,000; SIGNATURE_PERF_COUNT adds one more on-demand data
        // point (e.g. 50,000) without editing this file for occasional larger measurements.
        public static IEnumerable<object[]> ScaleCases()
        {
            yield return new object[] { 1000 };
            yield return new object[] { 5000 };

            if (int.TryParse(Environment.GetEnvironmentVariable("SIGNATURE_PERF_COUNT"), out var extra) && extra > 0)
                yield return new object[] { extra };
        }

        [Theory]
        [MemberData(nameof(ScaleCases))]
        public async Task Verification_AtScale_ReportsTimings(int count)
        {
            var ids = SeedValidChainedRecords(count);
            _output.WriteLine($"Seeded {count:N0} valid, chained SignatureRecords.");

            var verificationService = new SignatureVerificationService(_dbFixture.Context, _hmacService);

            // 1. Single verification (recompute canonical + HMAC + one signer-chain load).
            var singleSw = Stopwatch.StartNew();
            var singleStatus = await verificationService.GetVerificationStatusAsync(ids[count / 2]);
            singleSw.Stop();
            Assert.Equal("Valid", singleStatus!.Status);
            _output.WriteLine($"Single verification: {singleSw.Elapsed.TotalMilliseconds:F2} ms");

            // 2. Batch of 100 (the controller's MaxBatchSize).
            var batchIds = ids.Take(Math.Min(100, count)).ToList();
            var batchSw = Stopwatch.StartNew();
            var batchResults = await verificationService.GetVerificationStatusBatchAsync(batchIds);
            batchSw.Stop();
            Assert.All(batchResults, r => Assert.Equal("Valid", r.Status));
            _output.WriteLine($"Batch of {batchIds.Count}: {batchSw.Elapsed.TotalMilliseconds:F2} ms " +
                              $"({batchSw.Elapsed.TotalMilliseconds / batchIds.Count:F3} ms/record)");

            // 3. Full sweep over all records (what the background job does).
            var sweeper = new SignatureVerificationSweeper(
                _dbFixture.Context, verificationService, Mock.Of<ILogger<SignatureVerificationSweeper>>());
            var sweepSw = Stopwatch.StartNew();
            var summary = await sweeper.RunAsync(CancellationToken.None);
            sweepSw.Stop();

            _output.WriteLine($"Full sweep of {count:N0}: {sweepSw.Elapsed.TotalMilliseconds:F0} ms " +
                              $"({sweepSw.Elapsed.TotalMilliseconds / count:F3} ms/record), " +
                              $"anomalies={summary.AnomaliesFound}, batchesFailed={summary.BatchesFailed}");

            // Correctness at scale: everything got checked, all valid.
            Assert.Equal(count, summary.RecordsChecked);
            Assert.Equal(0, summary.AnomaliesFound);
            Assert.Equal(0, summary.BatchesFailed);

            // Sanity only — catches a pathological (e.g. accidental O(n^2)) blow-up, not a modest
            // hardware difference. Deliberately very generous.
            Assert.True(sweepSw.Elapsed < TimeSpan.FromMinutes(10),
                $"Sweep of {count:N0} records took {sweepSw.Elapsed}, which is unexpectedly slow.");
        }

        // Bulk-inserts `count` SignatureRecords with real, correctly-chained HMACs directly (not via
        // the document signing flow, which would generate a PDF per record). Records are spread
        // across a small pool of signers/documents and chained per signer so they verify as Valid.
        private List<Guid> SeedValidChainedRecords(int count)
        {
            const int signerPoolSize = 50;
            const int insertBatchSize = 2000;

            var function = new Function { Id = Guid.NewGuid(), Name = "Operator", CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Functions.Add(function);

            var signers = new List<User>();
            var documents = new List<UserDocument>();
            for (var i = 0; i < signerPoolSize; i++)
            {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Signer",
                    LastName = $"Number{i}",
                    Email = $"signer{i}.{Guid.NewGuid():N}@example.com",
                    PersonalId = Guid.NewGuid().ToString(),
                    FunctionId = function.Id,
                    CreatedAt = DateTime.UtcNow
                };
                signers.Add(user);
                documents.Add(new UserDocument
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    DocumentType = "SU",
                    Status = "Completed",
                    GeneratedAt = DateTime.UtcNow,
                    DocumentHash = "seed-hash"
                });
            }
            _dbFixture.Context.Users.AddRange(signers);
            _dbFixture.Context.UserDocuments.AddRange(documents);
            _dbFixture.Context.SaveChanges();
            _dbFixture.Context.ChangeTracker.Clear();

            var ids = new List<Guid>(count);
            var lastHmacBySigner = new Dictionary<Guid, string?>();
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var version = SignatureCanonicalSerializer.CurrentVersion;

            for (var i = 0; i < count; i++)
            {
                var signer = signers[i % signerPoolSize];
                var document = documents[i % signerPoolSize];
                var fullName = $"{signer.FirstName} {signer.LastName}";
                var signedAt = baseTime.AddSeconds(i);
                var previousHash = lastHmacBySigner.GetValueOrDefault(signer.Id);

                var input = new SignatureCanonicalInput(
                    signer.Id, fullName, "Operator", signer.BadgeNumber,
                    "Norme SSM", 2m, new DateTime(2026, 1, 15),
                    signedAt, previousHash, version);
                var hmac = _hmacService.ComputeHmacAsync(SignatureCanonicalSerializer.Serialize(input))
                    .GetAwaiter().GetResult();
                lastHmacBySigner[signer.Id] = hmac;

                var recordId = Guid.NewGuid();
                ids.Add(recordId);
                _dbFixture.Context.SignatureRecords.Add(new SignatureRecord
                {
                    Id = recordId,
                    UserDocumentId = document.Id,
                    PeriodicTrainingId = null,
                    SignerRole = "User",
                    SignerUserId = signer.Id,
                    SignerFullNameSnapshot = fullName,
                    SignerPositionSnapshot = "Operator",
                    SignatureMethod = "Draw",
                    SignatureData = "sig",
                    MaterialTaughtSnapshot = "Norme SSM",
                    DurationHoursSnapshot = 2m,
                    TrainingDateSnapshot = new DateTime(2026, 1, 15),
                    SignedAt = signedAt,
                    PreviousSignatureHash = previousHash,
                    SignatureHmac = hmac,
                    IsLegacyUnverified = false,
                    Version = version
                });

                if ((i + 1) % insertBatchSize == 0)
                {
                    _dbFixture.Context.SaveChanges();
                    _dbFixture.Context.ChangeTracker.Clear();
                }
            }

            _dbFixture.Context.SaveChanges();
            _dbFixture.Context.ChangeTracker.Clear();
            return ids;
        }
    }
}
