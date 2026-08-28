using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Documents
{
    public class DocumentSignatureServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private DocumentSignatureService CreateService() => new(_dbFixture.Context);

        private DocumentSignatureToken SeedToken(bool isUsed = false, DateTime? expiresAt = null)
        {
            var token = new DocumentSignatureToken
            {
                Email = "signer@example.com",
                DocumentId = Guid.NewGuid(),
                DocumentName = "Test Document",
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
                IsUsed = isUsed
            };
            _dbFixture.Context.DocumentSignatureTokens.Add(token);
            _dbFixture.Context.SaveChanges();
            return token;
        }

        [Fact]
        public async Task ConsumeTokenAsync_ValidToken_ReturnsTrueAndMarksUsed()
        {
            var token = SeedToken();
            var service = CreateService();

            var result = await service.ConsumeTokenAsync(token.Token);

            Assert.True(result);
            // AsNoTracking: ExecuteUpdateAsync bypasses the change tracker, so a tracked read would be stale.
            var reloaded = await _dbFixture.Context.DocumentSignatureTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
            Assert.True(reloaded.IsUsed);
        }

        [Fact]
        public async Task ConsumeTokenAsync_AlreadyUsedToken_ReturnsFalse()
        {
            var token = SeedToken(isUsed: true);
            var service = CreateService();

            var result = await service.ConsumeTokenAsync(token.Token);

            Assert.False(result);
        }

        [Fact]
        public async Task ConsumeTokenAsync_ExpiredToken_ReturnsFalse()
        {
            var token = SeedToken(expiresAt: DateTime.UtcNow.AddDays(-1));
            var service = CreateService();

            var result = await service.ConsumeTokenAsync(token.Token);

            Assert.False(result);
        }

        [Fact]
        public async Task ConsumeTokenAsync_UnknownToken_ReturnsFalse()
        {
            var service = CreateService();

            var result = await service.ConsumeTokenAsync("does-not-exist");

            Assert.False(result);
        }

        [Fact]
        public async Task ConsumeTokenAsync_SecondCallOnSameToken_ReturnsFalse()
        {
            var token = SeedToken();
            var service = CreateService();

            var first = await service.ConsumeTokenAsync(token.Token);
            var second = await service.ConsumeTokenAsync(token.Token);

            Assert.True(first);
            Assert.False(second);
        }

        // Two separate DbContexts (as concurrent requests would get) must not both consume the token.
        [Fact]
        public async Task ConsumeTokenAsync_ConcurrentCallsOnSameToken_OnlyOneSucceeds()
        {
            var dbName = $"consume_race_{Guid.NewGuid():N}";
            var connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared&Default Timeout=5";

            // Anchor connection keeps the shared in-memory DB alive for the other two.
            using var anchor = new SqliteConnection(connectionString);
            anchor.Open();

            using var connection1 = new SqliteConnection(connectionString);
            using var connection2 = new SqliteConnection(connectionString);
            connection1.Open();
            connection2.Open();

            var options1 = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection1).Options;
            var options2 = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection2).Options;

            using var context1 = new ApplicationDbContext(options1, NullLogger<ApplicationDbContext>.Instance);
            using var context2 = new ApplicationDbContext(options2, NullLogger<ApplicationDbContext>.Instance);
            context1.Database.EnsureCreated();

            var token = new DocumentSignatureToken
            {
                Email = "signer@example.com",
                DocumentId = Guid.NewGuid(),
                DocumentName = "Test Document",
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            context1.DocumentSignatureTokens.Add(token);
            await context1.SaveChangesAsync();

            var service1 = new DocumentSignatureService(context1);
            var service2 = new DocumentSignatureService(context2);

            var results = await Task.WhenAll(
                service1.ConsumeTokenAsync(token.Token),
                service2.ConsumeTokenAsync(token.Token));

            Assert.Single(results, r => r);
        }
    }
}
