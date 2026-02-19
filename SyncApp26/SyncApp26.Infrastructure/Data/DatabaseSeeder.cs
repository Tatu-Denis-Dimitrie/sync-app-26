using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace SyncApp26.Infrastructure.Data
{
    public static class DatabaseSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            // Check if data already exists
            if (await context.Departments.AnyAsync() || await context.Users.AnyAsync())
            {
                return; // Database has been seeded
            }

            // Create 5 departments
            var departments = new List<Department>
            {
                new Department
                {
                    Id = Guid.NewGuid(),
                    Name = "Engineering",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new Department
                {
                    Id = Guid.NewGuid(),
                    Name = "Human Resources",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new Department
                {
                    Id = Guid.NewGuid(),
                    Name = "Sales",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new Department
                {
                    Id = Guid.NewGuid(),
                    Name = "Marketing",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new Department
                {
                    Id = Guid.NewGuid(),
                    Name = "Finance",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                }
            };

            await context.Departments.AddRangeAsync(departments);
            await context.SaveChangesAsync();

            // Create 20 users with relationships
            var users = new List<User>
            {
                // Engineering Department (8 users)
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "John",
                    LastName = "Smith",
                    Email = "john.smith@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // CTO - no manager
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Sarah",
                    LastName = "Johnson",
                    Email = "sarah.johnson@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to John later
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Michael",
                    LastName = "Brown",
                    Email = "michael.brown@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to John later
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Emily",
                    LastName = "Davis",
                    Email = "emily.davis@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to Sarah later
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "David",
                    LastName = "Wilson",
                    Email = "david.wilson@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to Sarah later
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jessica",
                    LastName = "Martinez",
                    Email = "jessica.martinez@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to Michael later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Daniel",
                    LastName = "Garcia",
                    Email = "daniel.garcia@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to Michael later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Ashley",
                    LastName = "Rodriguez",
                    Email = "ashley.rodriguez@syncapp.com",
                    DepartmentId = departments[0].Id,
                    AssignedToId = null, // Will be set to Sarah later
                    CreatedAt = DateTime.UtcNow.AddMonths(-2)
                },

                // Human Resources Department (3 users)
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Lisa",
                    LastName = "Anderson",
                    Email = "lisa.anderson@syncapp.com",
                    DepartmentId = departments[1].Id,
                    AssignedToId = null, // HR Director
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Robert",
                    LastName = "Taylor",
                    Email = "robert.taylor@syncapp.com",
                    DepartmentId = departments[1].Id,
                    AssignedToId = null, // Will be set to Lisa later
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Amanda",
                    LastName = "Thomas",
                    Email = "amanda.thomas@syncapp.com",
                    DepartmentId = departments[1].Id,
                    AssignedToId = null, // Will be set to Lisa later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },

                // Sales Department (4 users)
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Christopher",
                    LastName = "Moore",
                    Email = "christopher.moore@syncapp.com",
                    DepartmentId = departments[2].Id,
                    AssignedToId = null, // Sales Director
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicole",
                    LastName = "Jackson",
                    Email = "nicole.jackson@syncapp.com",
                    DepartmentId = departments[2].Id,
                    AssignedToId = null, // Will be set to Christopher later
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Matthew",
                    LastName = "White",
                    Email = "matthew.white@syncapp.com",
                    DepartmentId = departments[2].Id,
                    AssignedToId = null, // Will be set to Christopher later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jennifer",
                    LastName = "Harris",
                    Email = "jennifer.harris@syncapp.com",
                    DepartmentId = departments[2].Id,
                    AssignedToId = null, // Will be set to Christopher later
                    CreatedAt = DateTime.UtcNow.AddMonths(-2)
                },

                // Marketing Department (3 users)
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Ryan",
                    LastName = "Martin",
                    Email = "ryan.martin@syncapp.com",
                    DepartmentId = departments[3].Id,
                    AssignedToId = null, // Marketing Director
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Lauren",
                    LastName = "Thompson",
                    Email = "lauren.thompson@syncapp.com",
                    DepartmentId = departments[3].Id,
                    AssignedToId = null, // Will be set to Ryan later
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Kevin",
                    LastName = "Lee",
                    Email = "kevin.lee@syncapp.com",
                    DepartmentId = departments[3].Id,
                    AssignedToId = null, // Will be set to Ryan later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },

                // Finance Department (2 users)
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Michelle",
                    LastName = "Walker",
                    Email = "michelle.walker@syncapp.com",
                    DepartmentId = departments[4].Id,
                    AssignedToId = null, // CFO
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Brian",
                    LastName = "Hall",
                    Email = "brian.hall@syncapp.com",
                    DepartmentId = departments[4].Id,
                    AssignedToId = null, // Will be set to Michelle later
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                }
            };

            // Set up the management hierarchy
            // Engineering: John is CTO, Sarah and Michael are team leads under John, others under team leads
            users[1].AssignedToId = users[0].Id; // Sarah reports to John
            users[2].AssignedToId = users[0].Id; // Michael reports to John
            users[3].AssignedToId = users[1].Id; // Emily reports to Sarah
            users[4].AssignedToId = users[1].Id; // David reports to Sarah
            users[5].AssignedToId = users[2].Id; // Jessica reports to Michael
            users[6].AssignedToId = users[2].Id; // Daniel reports to Michael
            users[7].AssignedToId = users[1].Id; // Ashley reports to Sarah

            // HR: Lisa is director, others report to her
            users[9].AssignedToId = users[8].Id;  // Robert reports to Lisa
            users[10].AssignedToId = users[8].Id; // Amanda reports to Lisa

            // Sales: Christopher is director, others report to him
            users[12].AssignedToId = users[11].Id; // Nicole reports to Christopher
            users[13].AssignedToId = users[11].Id; // Matthew reports to Christopher
            users[14].AssignedToId = users[11].Id; // Jennifer reports to Christopher

            // Marketing: Ryan is director, others report to him
            users[16].AssignedToId = users[15].Id; // Lauren reports to Ryan
            users[17].AssignedToId = users[15].Id; // Kevin reports to Ryan

            // Finance: Michelle is CFO, Brian reports to her
            users[19].AssignedToId = users[18].Id; // Brian reports to Michelle

            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();

            Console.WriteLine("Database seeded successfully!");
            Console.WriteLine($"Created {departments.Count} departments and {users.Count} users.");
        }
    }
}
