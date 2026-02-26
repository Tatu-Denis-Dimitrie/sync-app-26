using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Infrastructure.Context
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<ImportConflict> ImportConflicts { get; set; }
        public DbSet<ImportHistory> ImportHistories { get; set; }
        public DbSet<Function> Functions { get; set; }
        public DbSet<DepartmentFunction> DepartmentFunctions { get; set; }

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

            // Configure ImportConflict entity
            modelBuilder.Entity<ImportConflict>(entity =>
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
                    .IsRequired()
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
        }
    }
}
