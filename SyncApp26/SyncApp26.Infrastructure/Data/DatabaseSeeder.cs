using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;

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

            // Create 3 roles first
            var roles = new List<Role>
            {
                new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Admin",
                    Description = "Full system access with administrative privileges",
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Line Manager",
                    Description = "Can manage assigned team members",
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Basic User",
                    Description = "Standard user with basic access",
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                }
            };

            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();

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

            var functions = new List<Function>
            {
                // Engineering functions
                new() { Id = Guid.NewGuid(), Name = "CTO", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
                new() { Id = Guid.NewGuid(), Name = "Team Lead", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
                new() { Id = Guid.NewGuid(), Name = "Software Engineer", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
                new() { Id = Guid.NewGuid(), Name = "QA Engineer", CreatedAt = DateTime.UtcNow.AddMonths(-6) },

                // Human Resources functions
                new() { Id = Guid.NewGuid(), Name = "HR Director", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
                new() { Id = Guid.NewGuid(), Name = "HR Specialist", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
                new() { Id = Guid.NewGuid(), Name = "Recruiter", CreatedAt = DateTime.UtcNow.AddMonths(-6) },

                // Sales functions
                new() { Id = Guid.NewGuid(), Name = "Sales Director", CreatedAt = DateTime.UtcNow.AddMonths(-5) },
                new() { Id = Guid.NewGuid(), Name = "Account Executive", CreatedAt = DateTime.UtcNow.AddMonths(-5) },
                new() { Id = Guid.NewGuid(), Name = "Sales Representative", CreatedAt = DateTime.UtcNow.AddMonths(-5) },

                // Marketing functions
                new() { Id = Guid.NewGuid(), Name = "Marketing Director", CreatedAt = DateTime.UtcNow.AddMonths(-5) },
                new() { Id = Guid.NewGuid(), Name = "Content Specialist", CreatedAt = DateTime.UtcNow.AddMonths(-5) },
                new() { Id = Guid.NewGuid(), Name = "Digital Marketing Specialist", CreatedAt = DateTime.UtcNow.AddMonths(-5) },

                // Finance functions
                new() { Id = Guid.NewGuid(), Name = "CFO", CreatedAt = DateTime.UtcNow.AddMonths(-4) },
                new() { Id = Guid.NewGuid(), Name = "Financial Analyst", CreatedAt = DateTime.UtcNow.AddMonths(-4) },
                new() { Id = Guid.NewGuid(), Name = "Accountant", CreatedAt = DateTime.UtcNow.AddMonths(-4) }
            };

            await context.Functions.AddRangeAsync(functions);
            await context.SaveChangesAsync();

            var departmentFunctions = new List<DepartmentFunction>
            {
                // Engineering
                new() { DepartmentId = departments[0].Id, FunctionId = functions[0].Id},
                new() { DepartmentId = departments[0].Id, FunctionId = functions[1].Id},
                new() { DepartmentId = departments[0].Id, FunctionId = functions[2].Id},
                new() { DepartmentId = departments[0].Id, FunctionId = functions[3].Id},

                // Human Resources
                new() { DepartmentId = departments[1].Id, FunctionId = functions[4].Id },
                new() { DepartmentId = departments[1].Id, FunctionId = functions[5].Id },
                new() { DepartmentId = departments[1].Id, FunctionId = functions[6].Id },

                // Sales
                new() { DepartmentId = departments[2].Id, FunctionId = functions[7].Id },
                new() { DepartmentId = departments[2].Id, FunctionId = functions[8].Id },
                new() { DepartmentId = departments[2].Id, FunctionId = functions[9].Id },

                // Marketing
                new() { DepartmentId = departments[3].Id, FunctionId = functions[10].Id },
                new() { DepartmentId = departments[3].Id, FunctionId = functions[11].Id },
                new() { DepartmentId = departments[3].Id, FunctionId = functions[12].Id },

                // Finance
                new() { DepartmentId = departments[4].Id, FunctionId = functions[13].Id },
                new() { DepartmentId = departments[4].Id, FunctionId = functions[14].Id },
                new() { DepartmentId = departments[4].Id, FunctionId = functions[15].Id },
            };

            await context.DepartmentFunctions.AddRangeAsync(departmentFunctions);
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
                    RoleId = roles[0].Id, // Admin - CTO
                    AssignedToId = null, // CTO - no manager
                    PersonalId = "fa88f377-32d3-4b03-9e2b-af6fbf44bbc1",
                    FunctionId = functions[0].Id, // CTO
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Sarah",
                    LastName = "Johnson",
                    Email = "sarah.johnson@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[1].Id, // Line Manager - Team Lead
                    AssignedToId = null,
                    PersonalId = "879eabaa-30bf-4ed7-8cac-6bffc73e2907",
                    FunctionId = functions[1].Id, // Team Lead
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Michael",
                    LastName = "Brown",
                    Email = "michael.brown@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[1].Id, // Line Manager - Team Lead
                    AssignedToId = null, // Will be set to John later
                    PersonalId = "bafc4e21-87f6-44cf-9b98-4d6fde993532",
                    FunctionId = functions[1].Id, // Team Lead
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Emily",
                    LastName = "Davis",
                    Email = "emily.davis@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Sarah later
                    PersonalId = "952896bd-30ad-4115-b887-9286a27e8961",
                    FunctionId = functions[2].Id, // Developer
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "David",
                    LastName = "Wilson",
                    Email = "david.wilson@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Sarah later
                    PersonalId = "e1a876ae-e51e-46d9-ae31-d88d9a01b0c0",
                    FunctionId = functions[2].Id, // Developer
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jessica",
                    LastName = "Martinez",
                    Email = "jessica.martinez@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Michael later
                    PersonalId = "f9d7c549-5663-45a9-ae55-d47bd2b17f2b",
                    FunctionId = functions[3].Id, // QA Engineer
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Daniel",
                    LastName = "Garcia",
                    Email = "daniel.garcia@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Michael later
                    PersonalId = "14641267-ab9b-4821-8ed8-a24d73df06a1",
                    FunctionId = functions[3].Id, // Sales Executive
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Ashley",
                    LastName = "Rodriguez",
                    Email = "ashley.rodriguez@syncapp.com",
                    DepartmentId = departments[0].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Sarah later
                    PersonalId = "8b6e5200-7297-4d37-ac33-cd80524accff",
                    FunctionId = functions[2].Id, // Developer
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
                    RoleId = roles[0].Id, // Admin - HR Director
                    AssignedToId = null, // HR Director
                    PersonalId = "68f43253-6de9-4d03-a832-5b0b1e95241d",
                    FunctionId = functions[4].Id, // HR Director
                    CreatedAt = DateTime.UtcNow.AddMonths(-6)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Robert",
                    LastName = "Taylor",
                    Email = "robert.taylor@syncapp.com",
                    DepartmentId = departments[1].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Lisa later
                    PersonalId = "bf24dc22-87c9-465d-8e66-d8087e7325c6",
                    FunctionId = functions[5].Id, // HR Specialist
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Amanda",
                    LastName = "Thomas",
                    Email = "amanda.thomas@syncapp.com",
                    DepartmentId = departments[1].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Lisa later
                    PersonalId = "977e646c-4513-46b3-9573-d4193c88547f",
                    FunctionId = functions[6].Id, // Recruiter
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
                    RoleId = roles[0].Id, // Admin - Sales Director
                    AssignedToId = null, // Sales Director
                    PersonalId = "6e18bafc-c605-4cab-a6b9-09b9cd5ed339",
                    FunctionId = functions[7].Id, // Sales Executive
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Nicole",
                    LastName = "Jackson",
                    Email = "nicole.jackson@syncapp.com",
                    DepartmentId = departments[2].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Christopher later
                    PersonalId = "2b944698-1432-4c60-9d15-3f5538acb522",
                    FunctionId = functions[8].Id, // Sales Manager
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Matthew",
                    LastName = "White",
                    Email = "matthew.white@syncapp.com",
                    DepartmentId = departments[2].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Christopher later
                    PersonalId = "153bd6c3-b24b-4755-b734-23b36ee1837f",
                    FunctionId = functions[9].Id, // Sales Representative
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jennifer",
                    LastName = "Harris",
                    Email = "jennifer.harris@syncapp.com",
                    DepartmentId = departments[2].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Christopher later
                    PersonalId = "5182389b-1696-46ed-9e20-f78d7fd69002",
                    FunctionId = functions[9].Id, // Sales Representative
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
                    RoleId = roles[0].Id, // Admin - Marketing Director
                    AssignedToId = null, // Marketing Director
                    PersonalId = "9c508f1f-001f-4472-b4f8-201ea563234b",
                    FunctionId = functions[10].Id, // Marketing Director
                    CreatedAt = DateTime.UtcNow.AddMonths(-5)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Lauren",
                    LastName = "Thompson",
                    Email = "lauren.thompson@syncapp.com",
                    DepartmentId = departments[3].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Ryan later
                    PersonalId = "75bdd628-0a03-4401-a46b-60b4cb599bc9",
                    FunctionId = functions[11].Id, // Marketing Specialist
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Kevin",
                    LastName = "Lee",
                    Email = "kevin.lee@syncapp.com",
                    DepartmentId = departments[3].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Ryan later
                    PersonalId = "475a5584-1187-4e82-b3da-a9d09dd567d3",
                    FunctionId = functions[12].Id, // Digital Marketing Specialist
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
                    RoleId = roles[0].Id, // Admin - CFO
                    AssignedToId = null, // CFO
                    PersonalId = "19a5faeb-8a06-4b54-ab73-ccf1100ad300",
                    FunctionId = functions[13].Id, // CFO
                    CreatedAt = DateTime.UtcNow.AddMonths(-4)
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Brian",
                    LastName = "Hall",
                    Email = "brian.hall@syncapp.com",
                    DepartmentId = departments[4].Id,
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null, // Will be set to Michelle later
                    PersonalId = "e887062a-1248-48f7-9734-ab75ceb63950",
                    FunctionId = functions[14].Id, // Financial Analyst
                    CreatedAt = DateTime.UtcNow.AddMonths(-3)
                },

                // Test Users for easy login
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Admin",
                    LastName = "User",
                    Email = "admin@syncapp.com",
                    DepartmentId = departments[0].Id, // Engineering
                    RoleId = roles[0].Id, // Admin
                    AssignedToId = null,
                    PersonalId = Guid.NewGuid().ToString(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                    IsEmailVerified = true,
                    FunctionId = functions[0].Id, // CTO
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Manager",
                    LastName = "User",
                    Email = "manager@syncapp.com",
                    DepartmentId = departments[0].Id, // Engineering
                    RoleId = roles[1].Id, // Line Manager
                    AssignedToId = null,
                    PersonalId = Guid.NewGuid().ToString(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("manager123"),
                    IsEmailVerified = true,
                    FunctionId = functions[1].Id, // Engineering Manager
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Basic",
                    LastName = "User",
                    Email = "user@syncapp.com",
                    DepartmentId = departments[0].Id, // Engineering
                    RoleId = roles[2].Id, // Basic User
                    AssignedToId = null,
                    PersonalId = Guid.NewGuid().ToString(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("user123"),
                    IsEmailVerified = true,
                    FunctionId = functions[2].Id, // Basic User
                    CreatedAt = DateTime.UtcNow
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
