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
        }
    }
}
