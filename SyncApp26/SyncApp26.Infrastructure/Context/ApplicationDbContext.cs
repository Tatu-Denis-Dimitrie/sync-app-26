using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Infrastructure.Context
{
    public class ApplicationDbContext : DbContext
    {
        private const int MaxSaveRetries = 5;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override int SaveChanges()
        {
            return ExecuteWithSqliteRetry(() => base.SaveChanges());
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            return ExecuteWithSqliteRetry(() => base.SaveChanges(acceptAllChangesOnSuccess));
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await ExecuteWithSqliteRetryAsync(() => base.SaveChangesAsync(cancellationToken));
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithSqliteRetryAsync(() => base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken));
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserChangeHistory> UserChangeHistories { get; set; }
        public DbSet<ImportHistory> ImportHistories { get; set; }
        public DbSet<DocumentSignatureToken> DocumentSignatureTokens { get; set; }
        public DbSet<Function> Functions { get; set; }
        public DbSet<DepartmentFunction> DepartmentFunctions { get; set; }
        public DbSet<UserDocument> UserDocuments { get; set; }
        public DbSet<PeriodicTraining> PeriodicTrainings { get; set; }
        public DbSet<UserSignature> UserSignatures { get; set; }
        public DbSet<UserSignatureHistory> UserSignatureHistories { get; set; }
        public DbSet<DataChangeRequest> DataChangeRequests { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.FirstName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.LastName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasMaxLength(255);

                // Add index on Email for fast lookups (most common query in CSV sync)
                entity.HasIndex(e => e.Email)
                    .IsUnique()
                    .HasDatabaseName("IX_Users_Email");

                // Add index on DeletedAt for soft delete filtering
                entity.HasIndex(e => e.DeletedAt)
                    .HasDatabaseName("IX_Users_DeletedAt");

                // Add composite index for department + soft delete queries
                entity.HasIndex(e => new { e.DepartmentId, e.DeletedAt })
                    .HasDatabaseName("IX_Users_DepartmentId_DeletedAt");

                entity.Property(e => e.PersonalId)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.CreatedAt)
                    .IsRequired();

                // Configure relationship with Department
                entity.HasOne(e => e.Department)
                    .WithMany(d => d.Users)
                    .HasForeignKey(e => e.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Configure relationship with Role
                entity.HasOne(e => e.Role)
                    .WithMany(r => r.Users)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Configure self-referencing relationship for line manager
                entity.HasOne(e => e.AssignedTo)
                    .WithMany(u => u.AssignedUsers)
                    .HasForeignKey(e => e.AssignedToId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Create indexes
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.DepartmentId);
                entity.HasIndex(e => e.PersonalId).IsUnique();
            });

            // Configure Department entity
            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.HasIndex(e => e.Name).IsUnique();
            });

            // Configure Role entity
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Description)
                    .HasMaxLength(500);

                entity.HasIndex(e => e.Name).IsUnique();
            });

            // Configure UserChangeHistory entity
            modelBuilder.Entity<UserChangeHistory>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.FieldName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.OldValue)
                    .HasMaxLength(500);

                entity.Property(e => e.NewValue)
                    .HasMaxLength(500);

                entity.Property(e => e.Status)
                    .HasMaxLength(50);

                // Configure relationships
                entity.HasOne(e => e.ImportHistory)
                    .WithMany()
                    .HasForeignKey(e => e.ImportHistoryId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Create indexes
                entity.HasIndex(e => e.ImportHistoryId);
                entity.HasIndex(e => e.UserId);
            });

            // Configure ImportHistory entity
            modelBuilder.Entity<ImportHistory>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ImportDate)
                    .IsRequired();

                entity.Property(e => e.FileName)
                    .IsRequired()
                    .HasMaxLength(255);
            });

            // Configure Function entity
            modelBuilder.Entity<Function>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.HasIndex(e => e.Name).IsUnique();
            });

            // Configure DepartmentFunction entity
            modelBuilder.Entity<DepartmentFunction>(entity =>
            {
                entity.HasKey(e => new { e.DepartmentId, e.FunctionId });

                // Configure relationships
                entity.HasOne(df => df.Department)
                    .WithMany(d => d.DepartmentFunctions)
                    .HasForeignKey(df => df.DepartmentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(df => df.Function)
                    .WithMany(f => f.DepartmentFunctions)
                    .HasForeignKey(df => df.FunctionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure UserDocument entity
            modelBuilder.Entity<UserDocument>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.DocumentType)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                // Configure relationship with User
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Status);
            });

            // Configure UserSignature entity
            modelBuilder.Entity<UserSignature>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SignatureData).IsRequired();
                entity.Property(e => e.SignatureMethod).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SignatureHash).IsRequired().HasMaxLength(64);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // One active signature row per user
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_UserSignatures_UserId");
            });

            // Configure UserSignatureHistory entity (immutable audit log)
            modelBuilder.Entity<UserSignatureHistory>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SignatureData).IsRequired();
                entity.Property(e => e.SignatureMethod).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SignatureHash).IsRequired().HasMaxLength(64);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(20);
                entity.Property(e => e.PerformedByEmail).IsRequired().HasMaxLength(255);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_UserSignatureHistories_UserId");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_UserSignatureHistories_CreatedAt");
            });

            // Configure DataChangeRequest entity
            modelBuilder.Entity<DataChangeRequest>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RequestedChangesJson).IsRequired();
                entity.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ResolvedByAdmin)
                    .WithMany()
                    .HasForeignKey(e => e.ResolvedByAdminId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Status);
            });
        }

        private static bool IsSqliteLockException(Exception exception)
        {
            if (exception is not DbUpdateException dbUpdateException || dbUpdateException.InnerException is not SqliteException sqliteException)
            {
                return false;
            }

            return sqliteException.SqliteErrorCode == 5 || sqliteException.SqliteErrorCode == 6;
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            var delayMs = Math.Min(1000, 100 * attempt);
            return TimeSpan.FromMilliseconds(delayMs);
        }

        private static int ExecuteWithSqliteRetry(Func<int> action)
        {
            for (var attempt = 1; attempt <= MaxSaveRetries; attempt++)
            {
                try
                {
                    return action();
                }
                catch (Exception ex) when (attempt < MaxSaveRetries && IsSqliteLockException(ex))
                {
                    Thread.Sleep(GetRetryDelay(attempt));
                }
            }

            return action();
        }

        private static async Task<int> ExecuteWithSqliteRetryAsync(Func<Task<int>> action)
        {
            for (var attempt = 1; attempt <= MaxSaveRetries; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (attempt < MaxSaveRetries && IsSqliteLockException(ex))
                {
                    await Task.Delay(GetRetryDelay(attempt));
                }
            }

            return await action();
        }
    }
}
