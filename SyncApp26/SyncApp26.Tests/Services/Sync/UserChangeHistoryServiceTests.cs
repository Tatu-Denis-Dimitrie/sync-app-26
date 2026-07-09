using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Sync
{
    public class UserChangeHistoryServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private UserChangeHistoryService CreateService() => new(new UserChangeHistoryRepository(_dbFixture.Context));

        private User SeedUser()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "John",
                LastName = "Doe",
                Email = $"{Guid.NewGuid():N}@example.com",
                PersonalId = Guid.NewGuid().ToString(),
                Role = UserRole.BasicUser,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        private ImportHistory SeedImportHistory(string fileName = "users.csv")
        {
            var importHistory = new ImportHistory { Id = Guid.NewGuid(), ImportDate = DateTime.UtcNow, FileName = fileName };
            _dbFixture.Context.ImportHistories.Add(importHistory);
            _dbFixture.Context.SaveChanges();
            return importHistory;
        }

        private UserChangeHistory SeedHistory(Guid userId, Guid? importHistoryId = null, string fieldName = "firstname", DateTime? createdAt = null)
        {
            var history = new UserChangeHistory
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ImportHistoryId = importHistoryId,
                FieldName = fieldName,
                OldValue = "Old",
                NewValue = "New",
                Status = "accepted",
                CreatedAt = createdAt ?? DateTime.UtcNow
            };
            _dbFixture.Context.UserChangeHistories.Add(history);
            _dbFixture.Context.SaveChanges();
            return history;
        }

        // ───────────────────────── Get*: DTO mapping ─────────────────────────

        [Fact]
        public async Task GetAllUserChangeHistoriesAsync_LinkedImportHistory_MapsImportDateAndFileName()
        {
            var user = SeedUser();
            var importHistory = SeedImportHistory("payroll.csv");
            SeedHistory(user.Id, importHistory.Id);
            var service = CreateService();

            var result = (await service.GetAllUserChangeHistoriesAsync()).ToList();

            Assert.Single(result);
            Assert.Equal(importHistory.ImportDate, result[0].ImportDate);
            Assert.Equal("payroll.csv", result[0].ImportFileName);
        }

        [Fact]
        public async Task GetAllUserChangeHistoriesAsync_NoLinkedImportHistory_ImportFieldsAreNull()
        {
            var user = SeedUser();
            SeedHistory(user.Id, importHistoryId: null);
            var service = CreateService();

            var result = (await service.GetAllUserChangeHistoriesAsync()).ToList();

            Assert.Single(result);
            Assert.Null(result[0].ImportDate);
            Assert.Null(result[0].ImportFileName);
        }

        [Fact]
        public async Task GetUserChangeHistoryByIdAsync_NotFound_ReturnsNull()
        {
            var service = CreateService();

            var result = await service.GetUserChangeHistoryByIdAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetUserChangeHistoriesByImportHistoryIdAsync_FiltersToThatImportOnly()
        {
            var user = SeedUser();
            var importA = SeedImportHistory();
            var importB = SeedImportHistory();
            SeedHistory(user.Id, importA.Id, fieldName: "firstname");
            SeedHistory(user.Id, importA.Id, fieldName: "lastname");
            SeedHistory(user.Id, importB.Id, fieldName: "email");
            var service = CreateService();

            var result = (await service.GetUserChangeHistoriesByImportHistoryIdAsync(importA.Id)).ToList();

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal(importA.Id, r.ImportHistoryId));
        }

        [Fact]
        public async Task GetUserChangeHistoriesByUserIdAsync_FiltersToThatUserOnly()
        {
            var userA = SeedUser();
            var userB = SeedUser();
            SeedHistory(userA.Id);
            SeedHistory(userB.Id);
            var service = CreateService();

            var result = (await service.GetUserChangeHistoriesByUserIdAsync(userA.Id)).ToList();

            Assert.Single(result);
            Assert.Equal(userA.Id, result[0].UserId);
        }

        // ───────────────────────── AddUserChangeHistoryAsync ─────────────────────────

        [Fact]
        public async Task AddUserChangeHistoryAsync_IgnoresProvidedIdAndGeneratesNewOne()
        {
            var user = SeedUser();
            var service = CreateService();
            var suppliedId = Guid.NewGuid();
            var entry = new UserChangeHistory { Id = suppliedId, UserId = user.Id, FieldName = "firstname", OldValue = "Old", NewValue = "New", Status = "accepted", CreatedAt = DateTime.UtcNow };

            await service.AddUserChangeHistoryAsync(entry);

            Assert.Empty(_dbFixture.Context.UserChangeHistories.Where(h => h.Id == suppliedId));
            Assert.Single(_dbFixture.Context.UserChangeHistories.Where(h => h.UserId == user.Id));
        }

        [Fact]
        public async Task AddUserChangeHistoryAsync_DefaultCreatedAt_IsSetToUtcNow()
        {
            var user = SeedUser();
            var service = CreateService();
            var before = DateTime.UtcNow;
            var entry = new UserChangeHistory { UserId = user.Id, FieldName = "firstname", OldValue = "Old", NewValue = "New", Status = "accepted", CreatedAt = default };

            await service.AddUserChangeHistoryAsync(entry);

            var persisted = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.True(persisted.CreatedAt >= before);
        }

        [Fact]
        public async Task AddUserChangeHistoryAsync_ExplicitCreatedAt_IsPreserved()
        {
            var user = SeedUser();
            var service = CreateService();
            var explicitCreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entry = new UserChangeHistory { UserId = user.Id, FieldName = "firstname", OldValue = "Old", NewValue = "New", Status = "accepted", CreatedAt = explicitCreatedAt };

            await service.AddUserChangeHistoryAsync(entry);

            var persisted = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal(explicitCreatedAt, persisted.CreatedAt);
        }
    }
}
