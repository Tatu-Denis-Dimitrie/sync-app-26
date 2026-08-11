using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Shared.DTOs.DataChange;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Requests
{
    public class DataChangeRequestServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private DataChangeRequestService CreateService() =>
            new(new DataChangeRequestRepository(_dbFixture.Context), new UserChangeHistoryRepository(_dbFixture.Context));

        private User SeedUser(string firstName = "Old", Guid? departmentId = null, int? commuteDurationMinutes = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = "Doe",
                Email = $"{Guid.NewGuid():N}@example.com",
                PersonalId = Guid.NewGuid().ToString(),
                DepartmentId = departmentId,
                CommuteDurationMinutes = commuteDurationMinutes,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        private DataChangeRequest SeedRequest(Guid userId, string changesJson, string status = "Pending", string reason = "Because")
        {
            var request = new DataChangeRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RequestedChangesJson = changesJson,
                Reason = reason,
                Status = status,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.DataChangeRequests.Add(request);
            _dbFixture.Context.SaveChanges();
            return request;
        }

        // ResolvedByAdminId has a real FK to Users, so tests that reach a save must pass a persisted admin's Id.
        private Guid SeedAdmin() => SeedUser(firstName: "Admin").Id;

        // ───────────────────────── GetRequestByIdAsync / GetRequestsByUserAsync ─────────────────────────

        [Fact]
        public async Task GetRequestByIdAsync_ExistingRequest_MapsUserEmailAndFullName()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{}");
            var service = CreateService();

            var result = await service.GetRequestByIdAsync(request.Id);

            Assert.NotNull(result);
            Assert.Equal(user.Email, result.UserEmail);
            Assert.Equal($"{user.FirstName} {user.LastName}", result.UserFullName);
        }

        [Fact]
        public async Task GetRequestByIdAsync_NotFound_ReturnsNull()
        {
            var service = CreateService();

            var result = await service.GetRequestByIdAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetRequestsByUserAsync_ReturnsOnlyThatUsersRequestsNewestFirst()
        {
            var userA = SeedUser();
            var userB = SeedUser();
            var older = SeedRequest(userA.Id, "{}");
            older.CreatedAt = DateTime.UtcNow.AddDays(-2);
            var newer = SeedRequest(userA.Id, "{}");
            newer.CreatedAt = DateTime.UtcNow.AddDays(-1);
            _dbFixture.Context.SaveChanges();
            SeedRequest(userB.Id, "{}");
            var service = CreateService();

            var result = (await service.GetRequestsByUserAsync(userA.Id)).ToList();

            Assert.Equal(2, result.Count);
            Assert.Equal(newer.Id, result[0].Id);
            Assert.Equal(older.Id, result[1].Id);
        }

        // ───────────────────────── GetPendingCountAsync ─────────────────────────

        [Fact]
        public async Task GetPendingCountAsync_CountsOnlyPendingRequests()
        {
            var user = SeedUser();
            SeedRequest(user.Id, "{}", status: "Pending");
            SeedRequest(user.Id, "{}", status: "Pending");
            SeedRequest(user.Id, "{}", status: "Approved");
            SeedRequest(user.Id, "{}", status: "Rejected");
            var service = CreateService();

            var count = await service.GetPendingCountAsync();

            Assert.Equal(2, count);
        }

        // ───────────────────────── CreateRequestAsync ─────────────────────────

        [Fact]
        public async Task CreateRequestAsync_Success_PersistsPendingRequestWithUserLoaded()
        {
            var user = SeedUser();
            var service = CreateService();
            var dto = new CreateDataChangeRequestDTO { RequestedChangesJson = "{\"FirstName\":\"New\"}", Reason = "Name changed legally" };

            var result = await service.CreateRequestAsync(user.Id, dto);

            Assert.Equal("Pending", result.Status);
            Assert.Equal(user.Email, result.UserEmail);
            Assert.Equal($"{user.FirstName} {user.LastName}", result.UserFullName);

            _dbFixture.Context.ChangeTracker.Clear();
            var persisted = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == result.Id);
            Assert.Equal("Pending", persisted.Status);
            Assert.Equal("Name changed legally", persisted.Reason);
        }

        [Fact]
        public async Task CreateRequestAsync_Success_SnapshotsCurrentValueAsOriginalValuesJson()
        {
            var user = SeedUser(firstName: "Old");
            var service = CreateService();
            var dto = new CreateDataChangeRequestDTO { RequestedChangesJson = "{\"FirstName\":\"New\"}", Reason = "Name changed legally" };

            var result = await service.CreateRequestAsync(user.Id, dto);

            _dbFixture.Context.ChangeTracker.Clear();
            var persisted = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == result.Id);
            Assert.Equal("{\"FirstName\":\"Old\"}", persisted.OriginalValuesJson);
        }

        [Fact]
        public async Task CreateRequestAsync_NullReason_ThrowsDbUpdateException()
        {
            var user = SeedUser();
            var service = CreateService();
            var dto = new CreateDataChangeRequestDTO { RequestedChangesJson = "{}", Reason = null! };

            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateRequestAsync(user.Id, dto));
        }

        // ───────────────────────── ChangeStatusAsync ─────────────────────────

        [Fact]
        public async Task ChangeStatusAsync_ExistingRequest_UpdatesStatus()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{}");
            var service = CreateService();

            var result = await service.ChangeStatusAsync(request.Id, "Awaiting Verification");

            Assert.Equal("Awaiting Verification", result.Status);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Awaiting Verification", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ChangeStatusAsync_NotFound_ThrowsException()
        {
            var service = CreateService();

            var ex = await Assert.ThrowsAsync<Exception>(() => service.ChangeStatusAsync(Guid.NewGuid(), "Pending"));

            Assert.Equal("Request not found", ex.Message);
        }

        // ───────────────────────── ResolveRequestAsync: guards ─────────────────────────

        [Fact]
        public async Task ResolveRequestAsync_NotFound_ThrowsException()
        {
            var service = CreateService();

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), new ResolveDataChangeRequestDTO { Status = "Approved" }));

            Assert.Equal("Request not found", ex.Message);
        }

        [Fact]
        public async Task ResolveRequestAsync_AlreadyResolved_ThrowsException()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{}", status: "Approved");
            var service = CreateService();

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, Guid.NewGuid(), new ResolveDataChangeRequestDTO { Status = "Rejected" }));

            Assert.Equal("Request is already resolved", ex.Message);
        }

        // ───────────────────────── ResolveRequestAsync: applying changes to User ─────────────────────────

        [Theory]
        [InlineData("FirstName", "New")]
        [InlineData("DepartmentId", "11111111-1111-1111-1111-111111111111")]
        [InlineData("CommuteDurationMinutes", "45")]
        [InlineData("Address", "123 Main St")]
        [InlineData("BadgeNumber", "BADGE-001")]
        public async Task ResolveRequestAsync_Approved_AppliesSupportedPropertyType(string fieldName, string newValue)
        {
            var user = SeedUser();
            if (fieldName == "DepartmentId")
            {
                _dbFixture.Context.Departments.Add(new Department { Id = Guid.Parse(newValue), Name = "Target Dept", CreatedAt = DateTime.UtcNow });
                _dbFixture.Context.SaveChanges();
            }
            var request = SeedRequest(user.Id, $"{{\"{fieldName}\":\"{newValue}\"}}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var updatedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            var actual = fieldName switch
            {
                "FirstName" => updatedUser.FirstName,
                "DepartmentId" => updatedUser.DepartmentId?.ToString(),
                "CommuteDurationMinutes" => updatedUser.CommuteDurationMinutes?.ToString(),
                "Address" => updatedUser.Address,
                "BadgeNumber" => updatedUser.BadgeNumber,
                _ => null
            };
            Assert.Equal(newValue, actual);
        }

        [Fact]
        public async Task ResolveRequestAsync_Approved_AppliesEnumProperty()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{\"BloodType\":\"OPositive\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(BloodType.OPositive, _dbFixture.Context.Users.Single(u => u.Id == user.Id).BloodType);
        }

        [Fact]
        public async Task ResolveRequestAsync_UndefinedEnumValueForEnumProperty_SkipsSilently()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{\"BloodType\":\"NotARealBloodType\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            var result = await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            Assert.Equal("Approved", result.Status);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).BloodType);
        }

        [Fact]
        public async Task ResolveRequestAsync_Rejected_DoesNotApplyChangeButStampsResolutionMetadata()
        {
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"FirstName\":\"New\"}");
            var service = CreateService();
            var adminId = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, adminId, new ResolveDataChangeRequestDTO { Status = "Rejected" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Old", _dbFixture.Context.Users.Single(u => u.Id == user.Id).FirstName);
            var persistedRequest = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id);
            Assert.Equal("Rejected", persistedRequest.Status);
            Assert.Equal(adminId, persistedRequest.ResolvedByAdminId);
            Assert.NotNull(persistedRequest.ResolvedAt);
        }

        [Fact]
        public async Task ResolveRequestAsync_EmailInChanges_NeverAppliedEvenWhenApproved()
        {
            var user = SeedUser(firstName: "Old");
            var originalEmail = user.Email;
            var request = SeedRequest(user.Id, "{\"Email\":\"new@example.com\",\"FirstName\":\"New\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var updatedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            Assert.Equal(originalEmail, updatedUser.Email);
            Assert.Equal("New", updatedUser.FirstName);
        }

        [Fact]
        public async Task ResolveRequestAsync_RoleInChanges_NeverAppliedEvenWhenApproved()
        {
            // "Role" isn't a User property at all anymore (roles live in RoleAssignments), so
            // reflection can't find it in either the history-snapshot or apply loop - the same
            // defense-in-depth outcome as the old BlockedFields check, now enforced structurally: a
            // data change request can never grant itself a role, and leaves no trace of trying.
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{\"Role\":\"Admin\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.False(_dbFixture.Context.UserChangeHistories.Any(h => h.UserId == user.Id && h.FieldName == "Role"));
        }

        [Fact]
        public async Task ResolveRequestAsync_InvalidGuidStringForGuidProperty_SkipsSilently()
        {
            var department = new Department { Id = Guid.NewGuid(), Name = "Original Dept", CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.Departments.Add(department);
            _dbFixture.Context.SaveChanges();
            var user = SeedUser(departmentId: department.Id);
            var request = SeedRequest(user.Id, "{\"DepartmentId\":\"not-a-guid\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            var result = await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            Assert.Equal("Approved", result.Status);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(department.Id, _dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
        }

        // ───────────────────────── ResolveRequestAsync: change history ─────────────────────────

        [Fact]
        public async Task ResolveRequestAsync_ApprovedWithActualChange_CreatesHistoryEntry()
        {
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"FirstName\":\"New\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id && h.FieldName == "FirstName");
            Assert.Equal("Old", history.OldValue);
            Assert.Equal("New", history.NewValue);
            Assert.Equal("approved", history.Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_LiveValueAlreadyMatchesRequestBecauseOfExternalChange_StillCreatesHistoryFromSnapshot()
        {
            // Simulates the CSV-import race: something else (e.g. a CSV import) already changed the
            // user's LastName to the exact value this request asks for, before an admin resolves it.
            // Diffing against the live value alone would find no difference and silently skip the
            // history entry - the snapshot from request-creation time must be used instead.
            var user = SeedUser(firstName: "Old");
            user.LastName = "Fernandez"; // live value already matches what's requested
            _dbFixture.Context.SaveChanges();
            var request = SeedRequest(user.Id, "{\"LastName\":\"Fernandez\"}");
            request.OriginalValuesJson = "{\"LastName\":\"Garcia\"}"; // value at the time the request was created
            _dbFixture.Context.SaveChanges();
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id && h.FieldName == "LastName");
            Assert.Equal("Garcia", history.OldValue);
            Assert.Equal("Fernandez", history.NewValue);
            Assert.Equal("approved", history.Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_ValueUnchanged_DoesNotCreateHistoryEntry()
        {
            var user = SeedUser(firstName: "Same");
            var request = SeedRequest(user.Id, "{\"FirstName\":\"Same\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            Assert.Empty(_dbFixture.Context.UserChangeHistories.Where(h => h.UserId == user.Id));
        }

        [Fact]
        public async Task ResolveRequestAsync_Rejected_StillCreatesHistoryEntryForWhatWouldHaveChanged()
        {
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"FirstName\":\"New\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Rejected" });

            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id && h.FieldName == "FirstName");
            Assert.Equal("rejected", history.Status);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Old", _dbFixture.Context.Users.Single(u => u.Id == user.Id).FirstName);
        }

        // ───────────────────────── ResolveRequestAsync: malformed input ─────────────────────────

        [Fact]
        public async Task ResolveRequestAsync_UnknownPropertyKeyInJson_IsIgnoredWithoutError()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{\"NotARealProperty\":\"value\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            var result = await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            Assert.Equal("Approved", result.Status);
            Assert.Empty(_dbFixture.Context.UserChangeHistories.Where(h => h.UserId == user.Id));
        }

        [Fact]
        public async Task ResolveRequestAsync_MalformedJson_ThrowsWrappedException()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "not valid json");
            var service = CreateService();

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, Guid.NewGuid(), new ResolveDataChangeRequestDTO { Status = "Approved" }));

            Assert.Equal("Error processing data change request.", ex.Message);
            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }
    }
}
