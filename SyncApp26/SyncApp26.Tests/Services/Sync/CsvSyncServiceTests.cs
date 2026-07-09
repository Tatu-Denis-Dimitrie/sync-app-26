using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Shared.DTOs;
using SyncApp26.Shared.DTOs.CSV.Department;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Sync
{
    public class CsvSyncServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();
        private readonly Mock<ISyncNotificationService> _notificationMock = new();

        public void Dispose() => _dbFixture.Dispose();

        private CsvSyncService CreateService() => new(
            new UserRepository(_dbFixture.Context),
            new DepartmentRepository(_dbFixture.Context),
            new FunctionRepository(_dbFixture.Context),
            _notificationMock.Object,
            new ImportHistoryRepository(_dbFixture.Context),
            new UserChangeHistoryRepository(_dbFixture.Context));

        private Department SeedDepartment(string name = "Engineering", bool isActive = true)
        {
            var department = new Department { Id = Guid.NewGuid(), Name = name, IsActive = isActive, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Departments.Add(department);
            _dbFixture.Context.SaveChanges();
            return department;
        }

        private Function SeedFunction(string name)
        {
            var function = new Function { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Functions.Add(function);
            _dbFixture.Context.SaveChanges();
            return function;
        }

        private User SeedUser(string personalId, Guid departmentId, string firstName = "John", string lastName = "Doe",
            string? email = null, Guid? functionId = null, Guid? assignedToId = null, DateTime? updatedAt = null, UserRole role = UserRole.BasicUser)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = personalId,
                FirstName = firstName,
                LastName = lastName,
                Email = email ?? $"{personalId}@example.com",
                DepartmentId = departmentId,
                FunctionId = functionId,
                AssignedToId = assignedToId,
                Role = role,
                UpdatedAt = updatedAt,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        private static CsvUserDTO MakeCsvUser(string personalId, string firstName = "John", string lastName = "Doe",
            string? email = null, string departmentName = "Engineering", string? assignedToPersonalId = null, string? function = null) => new()
        {
            PersonalId = personalId,
            FirstName = firstName,
            LastName = lastName,
            Email = email ?? $"{personalId}@example.com",
            DepartmentName = departmentName,
            AssignedToPersonalId = assignedToPersonalId,
            Function = function
        };

        private static UserSyncItemDTO MakeNewItem(CsvUserDTO csvData) => new() { Id = Guid.NewGuid().ToString(), Status = "new", CsvData = csvData };

        private static UserSyncItemDTO MakeModifiedItem(Guid existingUserId, CsvUserDTO csvData, List<FieldConflictDTO>? conflicts = null) =>
            new() { Id = existingUserId.ToString(), Status = "modified", CsvData = csvData, Conflicts = conflicts ?? new() };

        private static UserSyncItemDTO MakeDeletedItem(Guid existingUserId) => new() { Id = existingUserId.ToString(), Status = "deleted" };

        // ───────────────────────── CompareWithDatabase ─────────────────────────

        [Fact]
        public async Task CompareWithDatabase_NewPersonalId_StatusNew()
        {
            SeedDepartment();
            var service = CreateService();

            var result = await service.CompareWithDatabase(new[] { MakeCsvUser("P-NEW") }, totalRows: 1);

            var comparison = Assert.Single(result);
            Assert.Equal("new", comparison.Status);
            Assert.True(comparison.Selected);
        }

        [Fact]
        public async Task CompareWithDatabase_MatchingPersonalIdNoDifferences_StatusUnchanged()
        {
            var department = SeedDepartment("Engineering");
            SeedUser("P1", department.Id, firstName: "John", lastName: "Doe", email: "john@example.com");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "John", lastName: "Doe", email: "john@example.com", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var comparison = Assert.Single(result);
            Assert.Equal("unchanged", comparison.Status);
            Assert.Empty(comparison.Conflicts);
            Assert.False(comparison.Selected);
        }

        [Fact]
        public async Task CompareWithDatabase_FirstNameDiffers_StatusModifiedWithConflict()
        {
            var department = SeedDepartment("Engineering");
            SeedUser("P1", department.Id, firstName: "John", lastName: "Doe", email: "john@example.com");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "Jane", lastName: "Doe", email: "john@example.com", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var comparison = Assert.Single(result);
            Assert.Equal("modified", comparison.Status);
            Assert.True(comparison.Selected);
            var conflict = Assert.Single(comparison.Conflicts);
            Assert.Equal("firstName", conflict.Field);
            Assert.Equal("John", conflict.DbValue);
            Assert.Equal("Jane", conflict.CsvValue);
        }

        [Fact]
        public async Task CompareWithDatabase_DbUserMissingFromCsv_StatusDeleted()
        {
            var department = SeedDepartment();
            SeedUser("P1", department.Id);
            var service = CreateService();

            var result = await service.CompareWithDatabase(Array.Empty<CsvUserDTO>(), totalRows: 0);

            var comparison = Assert.Single(result);
            Assert.Equal("deleted", comparison.Status);
            Assert.False(comparison.Selected);
        }

        [Fact]
        public async Task CompareWithDatabase_AssignedManagerNotActuallyLineManager_NameNotResolved()
        {
            var department = SeedDepartment();
            SeedUser("MGR1", department.Id, firstName: "Alice", lastName: "Boss"); // nobody reports to this user yet
            var service = CreateService();
            // MGR1 isn't present in the CSV, so it also surfaces as a separate "deleted" comparison entry -
            // the assertion below targets only the "new" entry for P-NEW.
            var csvUsers = new[] { MakeCsvUser("P-NEW", assignedToPersonalId: "MGR1") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var comparison = result.Single(c => c.Status == "new");
            Assert.Null(comparison.CsvUser!.AssignedToName);
        }

        // ───────────────────────── SyncUsers: new user ─────────────────────────

        [Fact]
        public async Task SyncUsers_NewUserValidDepartment_AddsUserAsBasicUser()
        {
            var department = SeedDepartment("Engineering");
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeNewItem(MakeCsvUser("P1", departmentName: "Engineering")) } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsProcessed);
            Assert.Equal(0, result.RecordsFailed);
            var persisted = _dbFixture.Context.Users.Single(u => u.PersonalId == "P1");
            Assert.Equal(UserRole.BasicUser, persisted.Role);
            Assert.Equal(department.Id, persisted.DepartmentId);
        }

        [Fact]
        public async Task SyncUsers_NewUserDepartmentInactive_RecordsFailureWithoutAdding()
        {
            SeedDepartment("Engineering", isActive: false);
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeNewItem(MakeCsvUser("P1", departmentName: "Engineering")) } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsFailed);
            Assert.Empty(_dbFixture.Context.Users.Where(u => u.PersonalId == "P1"));
        }

        [Fact]
        public async Task SyncUsers_NewUserWithFunction_ResolvesExistingFunctionByName()
        {
            SeedDepartment("Engineering");
            var function = SeedFunction("Welder");
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeNewItem(MakeCsvUser("P1", departmentName: "Engineering", function: "Welder")) } };

            await service.SyncUsers(request);

            var persisted = _dbFixture.Context.Users.Single(u => u.PersonalId == "P1");
            Assert.Equal(function.Id, persisted.FunctionId);
        }

        // ───────────────────────── SyncUsers: modified, with conflicts ─────────────────────────

        [Fact]
        public async Task SyncUsers_ModifiedConflictSelectedDb_RejectsChangeAndRecordsHistory()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Old");
            var service = CreateService();
            var conflict = new FieldConflictDTO { Field = "firstName", DbValue = "Old", CsvValue = "New", Selected = false };
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "New", departmentName: "Engineering"), new List<FieldConflictDTO> { conflict });
            var request = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(request);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Old", _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").FirstName);
            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal("rejected", history.Status);
            Assert.True(_dbFixture.Context.ImportHistories.Any());
        }

        [Fact]
        public async Task SyncUsers_ModifiedConflictSelectedCsv_AppliesChangeAndRecordsAcceptedHistory()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Old");
            var service = CreateService();
            var conflict = new FieldConflictDTO { Field = "firstName", DbValue = "Old", CsvValue = "New", Selected = true, SelectedValue = "csv" };
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "New", departmentName: "Engineering"), new List<FieldConflictDTO> { conflict });
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsProcessed);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("New", _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").FirstName);
            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal("accepted", history.Status);
        }

        [Fact]
        public async Task SyncUsers_ModifiedDepartmentConflictMissingDepartment_RecordsErrorWithoutApplying()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id);
            var service = CreateService();
            var conflict = new FieldConflictDTO { Field = "departmentname", DbValue = "Engineering", CsvValue = "Sales", Selected = true, SelectedValue = "csv" };
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", departmentName: "Sales"), new List<FieldConflictDTO> { conflict });
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Contains(result.Errors, e => e.Contains("does not exist or is inactive"));
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(department.Id, _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").DepartmentId);
        }

        // ───────────────────────── SyncUsers: modified, no conflicts (auto-diff) ─────────────────────────

        [Fact]
        public async Task SyncUsers_ModifiedNoConflicts_AppliesAllDifferingFieldsAutomatically()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Old");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "New", departmentName: "Engineering"));
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsProcessed);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("New", _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").FirstName);
        }

        [Fact]
        public async Task SyncUsers_ModifiedNoChanges_RecordsSkipped()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Same", lastName: "Same");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Same", lastName: "Same", departmentName: "Engineering"));
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsSkipped);
            Assert.Equal(0, result.RecordsProcessed);
        }

        [Fact]
        public async Task SyncUsers_ModifiedNoConflictsOnlyEmailDiffers_EmailNeverAutoApplied()
        {
            // The no-conflicts auto-diff branch only compares FirstName/LastName/Function/DepartmentName/AssignedTo -
            // Email is never checked there, so an email-only change is silently never applied through this path.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Same", lastName: "Same", email: "old@example.com");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Same", lastName: "Same", email: "new@example.com", departmentName: "Engineering"));
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsSkipped);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("old@example.com", _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").Email);
        }

        // ───────────────────────── SyncUsers: deleted (90-day grace period) ─────────────────────────

        [Fact]
        public async Task SyncUsers_DeletedUserRecentlyUpdated_SkipsWithinGracePeriod()
        {
            var department = SeedDepartment();
            var user = SeedUser("P1", department.Id, updatedAt: DateTime.UtcNow.AddDays(-10));
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeDeletedItem(user.Id) } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsSkipped);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.PersonalId == "P1").DeletedAt);
        }

        [Fact]
        public async Task SyncUsers_DeletedUserNeverUpdated_SoftDeletes()
        {
            var department = SeedDepartment();
            var user = SeedUser("P1", department.Id, updatedAt: null);
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeDeletedItem(user.Id) } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsProcessed);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.NotNull(_dbFixture.Context.Users.Single(u => u.PersonalId == "P1").DeletedAt);
        }

        // ───────────────────────── SyncUsers: line-manager promotion/demotion ─────────────────────────

        [Fact]
        public async Task SyncUsers_UserReferencedAsManager_PromotedToLineManager()
        {
            var department = SeedDepartment();
            var manager = SeedUser("MGR1", department.Id, role: UserRole.BasicUser);
            SeedUser("EMP1", department.Id, assignedToId: manager.Id);
            var service = CreateService();

            await service.SyncUsers(new SyncRequestDTO());

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(UserRole.LineManager, _dbFixture.Context.Users.Single(u => u.Id == manager.Id).Role);
        }

        [Fact]
        public async Task SyncUsers_LineManagerNoLongerReferenced_DemotedToBasicUser()
        {
            var department = SeedDepartment();
            var manager = SeedUser("MGR1", department.Id, role: UserRole.LineManager); // nobody reports to them
            var service = CreateService();

            await service.SyncUsers(new SyncRequestDTO());

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(UserRole.BasicUser, _dbFixture.Context.Users.Single(u => u.Id == manager.Id).Role);
        }

        // ───────────────────────── CompareDepartmentsWithDatabase / SyncDepartments ─────────────────────────

        [Theory]
        [InlineData("sales", "unchanged")]
        [InlineData("Marketing", "new")]
        public async Task CompareDepartmentsWithDatabase_DetectsNewVsUnchangedCaseInsensitively(string csvName, string expectedStatus)
        {
            SeedDepartment("Sales");
            var service = CreateService();

            var result = await service.CompareDepartmentsWithDatabase(new List<CSVDepartmentDTO> { new() { Name = csvName } });

            Assert.Equal(expectedStatus, Assert.Single(result).Status);
        }

        [Fact]
        public async Task SyncDepartments_MixedStatuses_OnlyAddsNewDepartments()
        {
            var service = CreateService();
            var list = new List<CSVDepartmentComparisionDTO>
            {
                new() { Status = "new", CsvDepartment = new CSVDepartmentDTO { Name = "Marketing" } },
                new() { Status = "unchanged", CsvDepartment = new CSVDepartmentDTO { Name = "Sales" } }
            };

            var result = await service.SyncDepartments(list);

            Assert.Equal(1, result.RecordsProcessed);
            Assert.Equal(1, result.RecordsSkipped);
            Assert.Single(_dbFixture.Context.Departments.Where(d => d.Name == "Marketing"));
            Assert.Empty(_dbFixture.Context.Departments.Where(d => d.Name == "Sales"));
        }
    }
}
