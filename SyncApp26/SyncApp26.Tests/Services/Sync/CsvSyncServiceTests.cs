using Microsoft.EntityFrameworkCore;
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

        private CsvSyncService CreateService()
        {
            // Mirrors the real AddRoleTables seed - CsvSyncService looks these up by name.
            _dbFixture.GetOrCreateRole(Roles.Admin);
            _dbFixture.GetOrCreateRole(Roles.LineManager);
            _dbFixture.GetOrCreateRole(Roles.BasicUser);

            return new(
                new UserRepository(_dbFixture.Context),
                new DepartmentRepository(_dbFixture.Context),
                new FunctionRepository(_dbFixture.Context),
                _notificationMock.Object,
                new ImportHistoryRepository(_dbFixture.Context),
                new UserChangeHistoryRepository(_dbFixture.Context),
                new DataChangeRequestRepository(_dbFixture.Context));
        }

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

        // isCsvManaged defaults to true because most tests here describe accounts that came from a
        // CSV import; pass false to model a seeded or self-registered account instead.
        private User SeedUser(string personalId, Guid departmentId, string firstName = "John", string lastName = "Doe",
            string? email = null, Guid? functionId = null, Guid? assignedToId = null, DateTime? updatedAt = null, string roleName = Roles.BasicUser,
            bool isCsvManaged = true)
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
                UpdatedAt = updatedAt,
                IsCsvManaged = isCsvManaged,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            _dbFixture.GrantRole(user, roleName);
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

        private DataChangeRequest SeedPendingRequest(Guid userId, string requestedChangesJson, string? originalValuesJson = null)
        {
            var request = new DataChangeRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RequestedChangesJson = requestedChangesJson,
                OriginalValuesJson = originalValuesJson,
                Reason = "Test",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.DataChangeRequests.Add(request);
            _dbFixture.Context.SaveChanges();
            return request;
        }

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
        public async Task CompareWithDatabase_NonCsvManagedUserMissingFromCsv_NotReportedAsDeleted()
        {
            // Seeded and self-registered accounts never appear in an HR export, so their absence
            // says nothing about them - they must not be offered up for deletion on every import.
            var department = SeedDepartment();
            SeedUser("SEEDED1", department.Id, isCsvManaged: false);
            var service = CreateService();

            var result = await service.CompareWithDatabase(Array.Empty<CsvUserDTO>(), totalRows: 0);

            Assert.Empty(result);
        }

        [Fact]
        public async Task CompareWithDatabase_RepeatedImportOfSameCsv_LeavesNonCsvManagedAccountsAlone()
        {
            // Regression: re-importing an unchanged CSV used to surface every seeded account as a
            // deletion candidate, so a "select all" click wiped accounts the CSV never owned.
            var department = SeedDepartment("Engineering");
            SeedUser("SEEDED1", department.Id, email: "admin@syncapp.com", isCsvManaged: false);
            SeedUser("P1", department.Id, firstName: "John", lastName: "Doe", email: "john@example.com");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "John", lastName: "Doe", email: "john@example.com", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            Assert.DoesNotContain(result, c => c.Status == "deleted");
            var comparison = Assert.Single(result);
            Assert.Equal("unchanged", comparison.Status);
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

        [Fact]
        public async Task CompareWithDatabase_FieldHasPendingRequest_ConflictFlagsHasPendingRequest()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Old");
            SeedPendingRequest(user.Id, "{\"FirstName\":\"RequestedNew\"}", "{\"FirstName\":\"Old\"}");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "CsvNew", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var conflict = Assert.Single(Assert.Single(result).Conflicts);
            Assert.Equal("firstName", conflict.Field);
            Assert.True(conflict.HasPendingRequest);
        }

        [Fact]
        public async Task CompareWithDatabase_FieldHasNoPendingRequest_ConflictDoesNotFlagHasPendingRequest()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Old");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "CsvNew", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var conflict = Assert.Single(Assert.Single(result).Conflicts);
            Assert.False(conflict.HasPendingRequest);
        }

        [Fact]
        public async Task CompareWithDatabase_StalePendingRequestButDbAndCsvAlreadyAgree_StillSurfacedAsConflict()
        {
            // The CSV already matches the DB (a prior import applied "Fernandez"), but there's still a
            // pending request for "Popescu" left over. Re-importing the same CSV must not make that
            // request invisible - it should keep showing up until an admin resolves it.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Fernandez");
            SeedPendingRequest(user.Id, "{\"LastName\":\"Popescu\"}", "{\"LastName\":\"Garcia\"}");
            var service = CreateService();
            var csvUsers = new[] { MakeCsvUser("P1", firstName: "Daniel", lastName: "Fernandez", departmentName: "Engineering") };

            var result = await service.CompareWithDatabase(csvUsers, totalRows: 1);

            var comparison = Assert.Single(result);
            Assert.Equal("modified", comparison.Status);
            var conflict = Assert.Single(comparison.Conflicts);
            Assert.Equal("lastName", conflict.Field);
            Assert.Equal("Fernandez", conflict.DbValue);
            Assert.Equal("Fernandez", conflict.CsvValue);
            Assert.True(conflict.HasPendingRequest);
            Assert.Equal("Popescu", conflict.PendingRequestValue);
        }

        [Fact]
        public async Task SyncUsers_StalePendingRequestInformationalConflict_DoesNotWriteNoOpRejectedHistory()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Fernandez");
            SeedPendingRequest(user.Id, "{\"LastName\":\"Popescu\"}", "{\"LastName\":\"Garcia\"}");
            var service = CreateService();
            // Simulates the frontend re-sending the informational conflict CompareWithDatabase produced.
            var conflict = new FieldConflictDTO { Field = "lastName", DbValue = "Fernandez", CsvValue = "Fernandez", Selected = false, HasPendingRequest = true, PendingRequestValue = "Popescu" };
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Daniel", lastName: "Fernandez", departmentName: "Engineering"), new List<FieldConflictDTO> { conflict });
            var syncRequest = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(syncRequest);

            Assert.Empty(_dbFixture.Context.UserChangeHistories.Where(h => h.UserId == user.Id));
        }

        // ───────────────────────── SyncUsers: pending DataChangeRequest interaction ─────────────────────────

        [Fact]
        public async Task SyncUsers_ImportAppliesSameValueAsPendingRequest_AutoApprovesRequestWithHistoryFromSnapshot()
        {
            // Reproduces the race condition: a user has a pending "change LastName" request, and before
            // an admin resolves it, a CSV import applies that exact same LastName. The request must not
            // silently collapse into a no-op - it should auto-close with a real audit trail.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Garcia");
            var request = SeedPendingRequest(user.Id, "{\"LastName\":\"Fernandez\"}", "{\"LastName\":\"Garcia\"}");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Daniel", lastName: "Fernandez", departmentName: "Engineering"));
            var syncRequest = new SyncRequestDTO { Items = { item }, FileName = "daniel-import.csv" };

            await service.SyncUsers(syncRequest);

            _dbFixture.Context.ChangeTracker.Clear();
            var persistedRequest = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id);
            Assert.Equal("Approved", persistedRequest.Status);
            Assert.Null(persistedRequest.ResolvedByAdminId);
            Assert.NotNull(persistedRequest.ResolvedAt);

            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id && h.FieldName == "lastname" && h.Status == "approved-by-import");
            Assert.Equal("Garcia", history.OldValue);
            Assert.Equal("Fernandez", history.NewValue);
            Assert.NotNull(history.ImportHistoryId);
            Assert.Equal(history.ImportHistoryId, persistedRequest.AutoResolvedByImportHistoryId);
        }

        [Fact]
        public async Task SyncUsers_TwoPendingRequestsOnSameField_OnlyTheOneMatchingImportedValueAutoResolves()
        {
            // Daniel has two pending LastName requests: an older one for "Popescu" and a newer one for
            // "Fernandez". A CSV import applies "Fernandez". Only the Fernandez request should close -
            // Popescu must stay pending and not be misrepresented as already satisfied.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Garcia");
            var popescuRequest = SeedPendingRequest(user.Id, "{\"LastName\":\"Popescu\"}", "{\"LastName\":\"Garcia\"}");
            var fernandezRequest = SeedPendingRequest(user.Id, "{\"LastName\":\"Fernandez\"}", "{\"LastName\":\"Garcia\"}");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Daniel", lastName: "Fernandez", departmentName: "Engineering"));
            var syncRequest = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(syncRequest);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Approved", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == fernandezRequest.Id).Status);
            var stillPending = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == popescuRequest.Id);
            Assert.Equal("Pending", stillPending.Status);
            Assert.Null(stillPending.AutoResolvedByImportHistoryId);
            Assert.Null(stillPending.ResolvedAt);
        }

        [Fact]
        public async Task SyncUsers_ImportAppliesDifferentValueThanPendingRequest_RequestStaysPending()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Garcia");
            var request = SeedPendingRequest(user.Id, "{\"LastName\":\"Fernandez\"}", "{\"LastName\":\"Garcia\"}");
            var service = CreateService();
            // CSV imports a third value - neither the DB's original nor the request's target.
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Daniel", lastName: "Smith", departmentName: "Engineering"));
            var syncRequest = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(syncRequest);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
            Assert.Equal("Smith", _dbFixture.Context.Users.Single(u => u.PersonalId == "P1").LastName);
        }

        [Fact]
        public async Task SyncUsers_PendingRequestOnUnrelatedField_UntouchedByImport()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Daniel", lastName: "Garcia");
            // Department changes aren't in the CSV<->request textual-comparison map, so this should never auto-close.
            var request = SeedPendingRequest(user.Id, "{\"DepartmentId\":\"11111111-1111-1111-1111-111111111111\"}", "{\"DepartmentId\":\"\"}");
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Daniel", lastName: "Garcia", departmentName: "Engineering"));
            var syncRequest = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(syncRequest);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
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
            var basicUserRole = _dbFixture.GetOrCreateRole(Roles.BasicUser);
            Assert.Contains(persisted.RoleAssignments, a => a.RoleId == basicUserRole.Id);
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

        // ───────────────────────── SyncUsers: CSV-managed adoption ─────────────────────────

        [Fact]
        public async Task SyncUsers_ModifiedNonCsvManagedUser_BecomesCsvManaged()
        {
            // Being named in the CSV is what puts an account under CSV management, so a pre-existing
            // account matched by an import is adopted and its later departure can be detected.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "John", isCsvManaged: false);
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Jane", departmentName: "Engineering"));
            var request = new SyncRequestDTO { Items = { item } };

            await service.SyncUsers(request);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.True(_dbFixture.Context.Users.Single(u => u.PersonalId == "P1").IsCsvManaged);
        }

        [Fact]
        public async Task SyncUsers_ModifiedNonCsvManagedUserWithNoFieldChanges_StillBecomesCsvManaged()
        {
            // Adoption must persist even when no field actually changed - otherwise the flag would be
            // set in memory and thrown away, leaving the account permanently undetectable as departed.
            var department = SeedDepartment("Engineering");
            var user = SeedUser("P1", department.Id, firstName: "Same", lastName: "Same", isCsvManaged: false);
            var service = CreateService();
            var item = MakeModifiedItem(user.Id, MakeCsvUser("P1", firstName: "Same", lastName: "Same", departmentName: "Engineering"));
            var request = new SyncRequestDTO { Items = { item } };

            var result = await service.SyncUsers(request);

            Assert.Equal(1, result.RecordsSkipped);
            _dbFixture.Context.ChangeTracker.Clear();
            var stored = _dbFixture.Context.Users.Single(u => u.PersonalId == "P1");
            Assert.True(stored.IsCsvManaged);
            Assert.Null(stored.UpdatedAt); // bookkeeping only - not a change to their data
        }

        [Fact]
        public async Task SyncUsers_NewUserFromCsv_IsCsvManaged()
        {
            SeedDepartment("Engineering");
            var service = CreateService();
            var request = new SyncRequestDTO { Items = { MakeNewItem(MakeCsvUser("P-NEW", departmentName: "Engineering")) } };

            await service.SyncUsers(request);

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.True(_dbFixture.Context.Users.Single(u => u.PersonalId == "P-NEW").IsCsvManaged);
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
            var manager = SeedUser("MGR1", department.Id, roleName: Roles.BasicUser);
            SeedUser("EMP1", department.Id, assignedToId: manager.Id);
            var service = CreateService();

            await service.SyncUsers(new SyncRequestDTO());

            _dbFixture.Context.ChangeTracker.Clear();
            var lineManagerRole = _dbFixture.GetOrCreateRole(Roles.LineManager);
            var updated = _dbFixture.Context.Users.Include(u => u.RoleAssignments).Single(u => u.Id == manager.Id);
            Assert.Contains(updated.RoleAssignments, a => a.RoleId == lineManagerRole.Id);
        }

        [Fact]
        public async Task SyncUsers_ManagerPromoted_UnrelatedRoleSurvivesImport()
        {
            // The many-to-many model's whole point: promoting someone to LineManager must never
            // disturb any other role they separately hold (e.g. an SSM officer duty).
            var department = SeedDepartment();
            var manager = SeedUser("MGR1", department.Id, roleName: Roles.SsmOfficer);
            SeedUser("EMP1", department.Id, assignedToId: manager.Id);
            var service = CreateService();

            await service.SyncUsers(new SyncRequestDTO());

            _dbFixture.Context.ChangeTracker.Clear();
            var ssmOfficerRole = _dbFixture.GetOrCreateRole(Roles.SsmOfficer);
            var lineManagerRole = _dbFixture.GetOrCreateRole(Roles.LineManager);
            var updated = _dbFixture.Context.Users.Include(u => u.RoleAssignments).Single(u => u.Id == manager.Id);
            Assert.Contains(updated.RoleAssignments, a => a.RoleId == ssmOfficerRole.Id);
            Assert.Contains(updated.RoleAssignments, a => a.RoleId == lineManagerRole.Id);
        }

        [Fact]
        public async Task SyncUsers_LineManagerNoLongerReferenced_LineManagerRoleRevoked()
        {
            // Revoking the role must not force BasicUser back on - the many-to-many model leaves
            // whatever other roles the person holds untouched (here: none, by construction).
            var department = SeedDepartment();
            var manager = SeedUser("MGR1", department.Id, roleName: Roles.LineManager); // nobody reports to them
            var service = CreateService();

            await service.SyncUsers(new SyncRequestDTO());

            _dbFixture.Context.ChangeTracker.Clear();
            var lineManagerRole = _dbFixture.GetOrCreateRole(Roles.LineManager);
            var updated = _dbFixture.Context.Users.Include(u => u.RoleAssignments).Single(u => u.Id == manager.Id);
            Assert.DoesNotContain(updated.RoleAssignments, a => a.RoleId == lineManagerRole.Id);
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
