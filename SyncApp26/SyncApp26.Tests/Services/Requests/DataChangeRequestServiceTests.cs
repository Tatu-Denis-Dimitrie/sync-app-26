using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Shared.DTOs.DataChange;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Requests
{
    public class DataChangeRequestServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private DataChangeRequestService CreateService() =>
            new(
                new DataChangeRequestRepository(_dbFixture.Context),
                new UserChangeHistoryRepository(_dbFixture.Context),
                new UserService(new UserRepository(_dbFixture.Context)),
                new DocumentSignatureService(_dbFixture.Context),
                new WorkSiteRepository(_dbFixture.Context),
                new DepartmentRepository(_dbFixture.Context),
                new FunctionRepository(_dbFixture.Context));

        private WorkSite SeedWorkSite(string name, bool isActive = true)
        {
            var workSite = new WorkSite { Id = Guid.NewGuid(), Name = name, IsActive = isActive, CreatedAt = DateTime.UtcNow };
            _dbFixture.Context.WorkSites.Add(workSite);
            _dbFixture.Context.SaveChanges();
            return workSite;
        }

        private Department SeedDepartment(string name, bool isActive = true)
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

        private User SeedUser(string firstName = "Old", Guid? departmentId = null, int? commuteDurationMinutes = null, Guid? workSiteId = null, Guid? functionId = null)
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
                WorkSiteId = workSiteId,
                FunctionId = functionId,
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

        // DateOfBirth is the one self-service field with a DateTime? type. Before date support
        // existed, approving it wrote a history entry and silently left the user untouched.
        [Fact]
        public async Task ResolveRequestAsync_Approved_AppliesNullableDateProperty()
        {
            var user = SeedUser();
            var request = SeedRequest(user.Id, "{\"DateOfBirth\":\"1990-01-15\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(new DateTime(1990, 1, 15), _dbFixture.Context.Users.Single(u => u.Id == user.Id).DateOfBirth);
        }

        // The browser sends "yyyy-MM-dd"; parsing must not depend on the server's culture, or a
        // day/month swap would silently store the wrong birth date.
        [Fact]
        public async Task ResolveRequestAsync_Approved_ParsesDateInvariantlyRegardlessOfServerCulture()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ro-RO");

                var user = SeedUser();
                var request = SeedRequest(user.Id, "{\"DateOfBirth\":\"2001-03-04\"}");
                var service = CreateService();
                var admin = SeedAdmin();

                await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

                _dbFixture.Context.ChangeTracker.Clear();
                Assert.Equal(new DateTime(2001, 3, 4), _dbFixture.Context.Users.Single(u => u.Id == user.Id).DateOfBirth);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        // A request that restates the user's existing date must not manufacture a history row —
        // the stored original is formatted the same way the request is, so the diff sees equality.
        [Fact]
        public async Task ResolveRequestAsync_UnchangedDate_RecordsNoHistoryEntry()
        {
            var user = SeedUser();
            user.DateOfBirth = new DateTime(1985, 6, 30);
            _dbFixture.Context.SaveChanges();

            var service = CreateService();
            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"DateOfBirth\":\"1985-06-30\"}",
                Reason = "No real change"
            });

            await service.ResolveRequestAsync(created.Id, SeedAdmin(), new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Empty(_dbFixture.Context.UserChangeHistories.Where(h => h.UserId == user.Id && h.FieldName == "DateOfBirth"));
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
        public async Task ResolveRequestAsync_EmailInChanges_AppliedWhenApproved()
        {
            // Only RequestEmailChangeAsync ever legitimately puts an "Email" key into a request
            // (the generic Create action strips it), so once it's there, approval should apply it
            // like any other field.
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Email\":\"new@example.com\",\"FirstName\":\"New\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var updatedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            Assert.Equal("new@example.com", updatedUser.Email);
            Assert.Equal("New", updatedUser.FirstName);
        }

        [Fact]
        public async Task ResolveRequestAsync_EmailAlreadyTakenByAnotherUser_ThrowsAndLeavesRequestPending()
        {
            // The address could've been claimed by someone else in the gap between the request being
            // made and an admin approving it - approval must fail cleanly, not silently succeed or
            // throw the request into a half-applied state.
            var user = SeedUser(firstName: "Old");
            SeedUser(firstName: "Other"); // occupies "new@example.com" via a distinct seeded user below
            var conflictingUser = _dbFixture.Context.Users.First(u => u.FirstName == "Other");
            conflictingUser.Email = "new@example.com";
            _dbFixture.Context.SaveChanges();

            var request = SeedRequest(user.Id, "{\"Email\":\"new@example.com\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            var untouchedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            Assert.NotEqual("new@example.com", untouchedUser.Email);
            var persistedRequest = _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id);
            Assert.Equal("Pending", persistedRequest.Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_WorkSiteInChanges_AppliedWhenApproved()
        {
            var workSite = SeedWorkSite("Main Plant");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"WorkSite\":\"Main Plant\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var updatedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            Assert.Equal(workSite.Id, updatedUser.WorkSiteId);
        }

        [Fact]
        public async Task ResolveRequestAsync_WorkSiteMatchedCaseInsensitively_AppliedWhenApproved()
        {
            var workSite = SeedWorkSite("Main Plant");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"WorkSite\":\"main plant\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(workSite.Id, _dbFixture.Context.Users.Single(u => u.Id == user.Id).WorkSiteId);
        }

        [Fact]
        public async Task ResolveRequestAsync_WorkSiteNoLongerExists_ThrowsAndLeavesRequestPending()
        {
            // Same hazard as an email being claimed in the meantime: the named site may have been
            // deleted between the request and the approval. Failing loudly beats silently clearing
            // the user's work site.
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"WorkSite\":\"Ghost Site\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).WorkSiteId);
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_WorkSiteNoLongerActive_ThrowsAndLeavesRequestPending()
        {
            SeedWorkSite("Retired Plant", isActive: false);
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"WorkSite\":\"Retired Plant\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).WorkSiteId);
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_Rejected_DoesNotApplyWorkSite()
        {
            SeedWorkSite("Main Plant");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"WorkSite\":\"Main Plant\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Rejected" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).WorkSiteId);
        }

        [Fact]
        public async Task ResolveRequestAsync_ApprovedWorkSite_RecordsHistoryUsingNamesNotIds()
        {
            // History is read by humans (and diffed against CSV imports, which carry names too), so
            // both sides of a work-site change must be names rather than raw GUIDs.
            var oldSite = SeedWorkSite("Old Plant");
            SeedWorkSite("Main Plant");
            var user = SeedUser(firstName: "Old", workSiteId: oldSite.Id);
            var service = CreateService();
            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"WorkSite\":\"Main Plant\"}",
                Reason = "Relocated"
            });
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(created.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal("WorkSite", history.FieldName);
            Assert.Equal("Old Plant", history.OldValue);
            Assert.Equal("Main Plant", history.NewValue);
        }

        [Fact]
        public async Task CreateRequestAsync_WorkSiteChange_SnapshotsCurrentWorkSiteName()
        {
            var oldSite = SeedWorkSite("Old Plant");
            var user = SeedUser(firstName: "Old", workSiteId: oldSite.Id);
            var service = CreateService();

            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"WorkSite\":\"Main Plant\"}",
                Reason = "Relocated"
            });

            Assert.Contains("\"WorkSite\":\"Old Plant\"", created.OriginalValuesJson);
        }

        [Fact]
        public async Task CreateRequestAsync_WorkSiteChangeForUserWithoutWorkSite_SnapshotsEmptyString()
        {
            var user = SeedUser(firstName: "Old");
            var service = CreateService();

            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"WorkSite\":\"Main Plant\"}",
                Reason = "Relocated"
            });

            Assert.Contains("\"WorkSite\":\"\"", created.OriginalValuesJson);
        }

        // ───────────────────────── Department / Function (same navigation-property hazard as WorkSite) ─────────────────────────

        [Fact]
        public async Task ResolveRequestAsync_DepartmentInChanges_AppliedWhenApproved()
        {
            // A department change travels as the department's *name*, so approving has to look the
            // name up and set the FK - the reflection-based applier can't write a navigation property.
            var department = SeedDepartment("Engineering");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"Engineering\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(department.Id, _dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
        }

        [Fact]
        public async Task ResolveRequestAsync_DepartmentMatchedCaseInsensitively_AppliedWhenApproved()
        {
            var department = SeedDepartment("Engineering");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"engineering\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(department.Id, _dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
        }

        [Fact]
        public async Task ResolveRequestAsync_DepartmentNoLongerExists_ThrowsAndLeavesRequestPending()
        {
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"Ghost Dept\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_DepartmentNoLongerActive_ThrowsAndLeavesRequestPending()
        {
            SeedDepartment("Retired Dept", isActive: false);
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"Retired Dept\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_Rejected_DoesNotApplyDepartment()
        {
            SeedDepartment("Engineering");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"Engineering\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Rejected" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).DepartmentId);
        }

        [Fact]
        public async Task ResolveRequestAsync_ApprovedDepartment_RecordsHistoryUsingNamesNotIds()
        {
            var oldDept = SeedDepartment("Old Dept");
            SeedDepartment("Engineering");
            var user = SeedUser(firstName: "Old", departmentId: oldDept.Id);
            var service = CreateService();
            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"Department\":\"Engineering\"}",
                Reason = "Transferred"
            });
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(created.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal("Department", history.FieldName);
            Assert.Equal("Old Dept", history.OldValue);
            Assert.Equal("Engineering", history.NewValue);
        }

        [Fact]
        public async Task ResolveRequestAsync_FunctionInChanges_AppliedWhenApproved()
        {
            // Function has no IsActive flag, so approval only checks existence, not deactivation.
            var function = SeedFunction("QA Engineer");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Function\":\"QA Engineer\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Equal(function.Id, _dbFixture.Context.Users.Single(u => u.Id == user.Id).FunctionId);
        }

        [Fact]
        public async Task ResolveRequestAsync_FunctionNoLongerExists_ThrowsAndLeavesRequestPending()
        {
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Function\":\"Ghost Function\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await Assert.ThrowsAsync<Exception>(() =>
                service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" }));

            _dbFixture.Context.ChangeTracker.Clear();
            Assert.Null(_dbFixture.Context.Users.Single(u => u.Id == user.Id).FunctionId);
            Assert.Equal("Pending", _dbFixture.Context.DataChangeRequests.Single(r => r.Id == request.Id).Status);
        }

        [Fact]
        public async Task ResolveRequestAsync_ApprovedFunction_RecordsHistoryUsingNamesNotIds()
        {
            var oldFunction = SeedFunction("Junior Engineer");
            SeedFunction("QA Engineer");
            var user = SeedUser(firstName: "Old", functionId: oldFunction.Id);
            var service = CreateService();
            var created = await service.CreateRequestAsync(user.Id, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"Function\":\"QA Engineer\"}",
                Reason = "Promoted"
            });
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(created.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var history = _dbFixture.Context.UserChangeHistories.Single(h => h.UserId == user.Id);
            Assert.Equal("Function", history.FieldName);
            Assert.Equal("Junior Engineer", history.OldValue);
            Assert.Equal("QA Engineer", history.NewValue);
        }

        [Fact]
        public async Task ResolveRequestAsync_DepartmentAndWorkSiteBothInChanges_BothApplied()
        {
            // Two navigation-name fields in the same request must both resolve and apply, not just the
            // first one encountered.
            var department = SeedDepartment("Engineering");
            var workSite = SeedWorkSite("Main Plant");
            var user = SeedUser(firstName: "Old");
            var request = SeedRequest(user.Id, "{\"Department\":\"Engineering\",\"WorkSite\":\"Main Plant\"}");
            var service = CreateService();
            var admin = SeedAdmin();

            await service.ResolveRequestAsync(request.Id, admin, new ResolveDataChangeRequestDTO { Status = "Approved" });

            _dbFixture.Context.ChangeTracker.Clear();
            var updatedUser = _dbFixture.Context.Users.Single(u => u.Id == user.Id);
            Assert.Equal(department.Id, updatedUser.DepartmentId);
            Assert.Equal(workSite.Id, updatedUser.WorkSiteId);
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
