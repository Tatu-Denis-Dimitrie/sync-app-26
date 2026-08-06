using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Services;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Documents
{
    public class PeriodicTrainingServiceTests : IDisposable
    {
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private PeriodicTrainingService CreateService() => new(_dbFixture.Context);

        private User SeedUser(string firstName, string lastName)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = firstName,
                LastName = lastName,
                Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                PersonalId = Guid.NewGuid().ToString(),
                Role = Domain.Enums.UserRole.BasicUser,
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.Users.Add(user);
            _dbFixture.Context.SaveChanges();
            return user;
        }

        // ───────────────────────── CreateAsync ─────────────────────────

        [Fact]
        public async Task CreateAsync_ValidInstructor_SnapshotsNameAndSetsInstructorId()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");

            var result = await service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = instructor.Id,
                MaterialTaught = "Norme SSM generale"
            });

            Assert.Equal(instructor.Id, result.InstructorId);
            Assert.Equal("Elena Marin", result.InstructorName);

            var stored = _dbFixture.Context.PeriodicTrainings.Single(pt => pt.Id == result.Id);
            Assert.Equal(instructor.Id, stored.InstructorId);
            Assert.Equal("Elena Marin", stored.InstructorName);
        }

        [Fact]
        public async Task CreateAsync_InstructorIsTrainee_ThrowsArgumentException()
        {
            var service = CreateService();
            var trainee = SeedUser("Adela", "Popescu");

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = trainee.Id
            }));
        }

        [Fact]
        public async Task CreateAsync_InstructorNotFound_ThrowsArgumentException()
        {
            var service = CreateService();
            var trainee = SeedUser("Adela", "Popescu");

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = Guid.NewGuid()
            }));
        }

        // ───────────────────────── UpdateAsync ─────────────────────────

        [Fact]
        public async Task UpdateAsync_ChangingInstructor_SnapshotsNewInstructorName()
        {
            var service = CreateService();
            var originalInstructor = SeedUser("Elena", "Marin");
            var newInstructor = SeedUser("Ion", "Dobre");
            var trainee = SeedUser("Adela", "Popescu");

            var created = await service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = originalInstructor.Id
            });

            var updated = await service.UpdateAsync(created.Id, new UpdatePeriodicTrainingDTO
            {
                InstructorId = newInstructor.Id
            });

            Assert.Equal(newInstructor.Id, updated.InstructorId);
            Assert.Equal("Ion Dobre", updated.InstructorName);
        }

        [Fact]
        public async Task UpdateAsync_InstructorIsTrainee_ThrowsArgumentException()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");
            var created = await service.CreateAsync(new CreatePeriodicTrainingDTO { UserId = trainee.Id, InstructorId = instructor.Id });

            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(created.Id, new UpdatePeriodicTrainingDTO
            {
                InstructorId = trainee.Id
            }));
        }

        // ───────────────────────── BulkCreateAsync ─────────────────────────

        [Fact]
        public async Task BulkCreateAsync_InstructorNotFound_ReturnsErrorNoRecordsCreated()
        {
            var service = CreateService();
            var trainee = SeedUser("Adela", "Popescu");

            var result = await service.BulkCreateAsync(new BulkCreatePeriodicTrainingDTO
            {
                InstructorId = Guid.NewGuid(),
                DocumentType = "SU",
                ApplyToAllUsers = false,
                SelectedUserIds = new List<Guid> { trainee.Id }
            });

            Assert.Equal(0, result.SuccessCount);
            Assert.Contains("Instructor not found.", result.Errors);
            Assert.Empty(_dbFixture.Context.PeriodicTrainings);
        }

        [Fact]
        public async Task BulkCreateAsync_InstructorIsOneOfSelectedTrainees_SkipsThatUserOnly()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");

            var result = await service.BulkCreateAsync(new BulkCreatePeriodicTrainingDTO
            {
                InstructorId = instructor.Id,
                DocumentType = "SU",
                ApplyToAllUsers = false,
                SelectedUserIds = new List<Guid> { trainee.Id, instructor.Id }
            });

            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Errors, e => e.Contains("cannot be their own instructor"));

            var traineeRow = Assert.Single(_dbFixture.Context.PeriodicTrainings.Where(pt => pt.UserId == trainee.Id));
            Assert.Equal(instructor.Id, traineeRow.InstructorId);
            Assert.DoesNotContain(_dbFixture.Context.PeriodicTrainings, pt => pt.UserId == instructor.Id);
        }

        [Fact]
        public async Task BulkCreateAsync_ValidInstructor_SetsInstructorIdAndNameForAllCreatedRows()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee1 = SeedUser("Adela", "Popescu");
            var trainee2 = SeedUser("Vlad", "Georgescu");

            var result = await service.BulkCreateAsync(new BulkCreatePeriodicTrainingDTO
            {
                InstructorId = instructor.Id,
                DocumentType = "SU",
                ApplyToAllUsers = false,
                SelectedUserIds = new List<Guid> { trainee1.Id, trainee2.Id }
            });

            Assert.Equal(2, result.SuccessCount);
            Assert.All(_dbFixture.Context.PeriodicTrainings, pt =>
            {
                Assert.Equal(instructor.Id, pt.InstructorId);
                Assert.Equal("Elena Marin", pt.InstructorName);
            });
        }

        // ───────────────────────── SetPrintExclusionAsync ─────────────────────────

        [Fact]
        public async Task SetPrintExclusionAsync_Excluded_SetsTimestampAndAdminId()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");
            var admin = SeedUser("Admin", "User");

            var created = await service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = instructor.Id
            });

            var result = await service.SetPrintExclusionAsync(created.Id, excluded: true, actingAdminId: admin.Id);

            Assert.NotNull(result.ExcludedFromPrintAt);

            var stored = _dbFixture.Context.PeriodicTrainings.Single(pt => pt.Id == created.Id);
            Assert.NotNull(stored.ExcludedFromPrintAt);
            Assert.Equal(admin.Id, stored.ExcludedFromPrintById);
        }

        [Fact]
        public async Task SetPrintExclusionAsync_NotExcluded_ClearsTimestamp()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");
            var admin = SeedUser("Admin", "User");

            var created = await service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = instructor.Id
            });

            await service.SetPrintExclusionAsync(created.Id, excluded: true, actingAdminId: admin.Id);
            var result = await service.SetPrintExclusionAsync(created.Id, excluded: false, actingAdminId: admin.Id);

            Assert.Null(result.ExcludedFromPrintAt);

            var stored = _dbFixture.Context.PeriodicTrainings.Single(pt => pt.Id == created.Id);
            Assert.Null(stored.ExcludedFromPrintAt);
            Assert.Null(stored.ExcludedFromPrintById);
        }

        [Fact]
        public async Task SetPrintExclusionAsync_RowWithCopies_ExcludesWholeFamily()
        {
            var service = CreateService();
            var instructor = SeedUser("Elena", "Marin");
            var trainee = SeedUser("Adela", "Popescu");
            var admin = SeedUser("Admin", "User");

            var root = await service.CreateAsync(new CreatePeriodicTrainingDTO
            {
                UserId = trainee.Id,
                InstructorId = instructor.Id
            });

            var copy = new PeriodicTraining
            {
                Id = Guid.NewGuid(),
                UserId = trainee.Id,
                SourceRowId = root.Id,
                InstructorId = instructor.Id,
                InstructorName = "Elena Marin",
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.PeriodicTrainings.Add(copy);
            await _dbFixture.Context.SaveChangesAsync();

            // Act on the copy — the root and any sibling copies must be excluded too.
            await service.SetPrintExclusionAsync(copy.Id, excluded: true, actingAdminId: admin.Id);

            var rootStored = _dbFixture.Context.PeriodicTrainings.Single(pt => pt.Id == root.Id);
            var copyStored = _dbFixture.Context.PeriodicTrainings.Single(pt => pt.Id == copy.Id);
            Assert.NotNull(rootStored.ExcludedFromPrintAt);
            Assert.NotNull(copyStored.ExcludedFromPrintAt);
            Assert.Equal(admin.Id, rootStored.ExcludedFromPrintById);
            Assert.Equal(admin.Id, copyStored.ExcludedFromPrintById);
        }

        [Fact]
        public async Task SetPrintExclusionAsync_UnknownId_ThrowsArgumentException()
        {
            var service = CreateService();
            var admin = SeedUser("Admin", "User");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SetPrintExclusionAsync(Guid.NewGuid(), excluded: true, actingAdminId: admin.Id));
        }
    }
}
