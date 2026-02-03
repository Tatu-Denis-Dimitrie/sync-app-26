using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs;
using SyncApp26.Shared.DTOs.Response.User;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.CSV.Department;
using SyncApp26.Shared.DTOs.Response.Department;

namespace SyncApp26.Application.Services;

public class CsvSyncService : ICsvSyncService
{
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public CsvSyncService(IUserRepository userRepository, IDepartmentRepository departmentRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<List<UserComparisonDTO>> CompareWithDatabase(List<CsvUserDTO> csvUsers)
    {
        var comparisons = new List<UserComparisonDTO>();
        var dbUsers = (await _userRepository.GetAllUsersAsync()).ToList();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        // Create a map of email to DB user for quick lookup
        var dbUserMap = dbUsers.ToDictionary(u => u.Email.ToLower(), u => u);

        // Process CSV users
        foreach (var csvUser in csvUsers)
        {
            var email = csvUser.Email.ToLower();

            if (dbUserMap.TryGetValue(email, out var dbUser))
            {
                // User exists - compare fields
                var csvManager = csvUser.AssignedToEmail != null
                    ? dbUsers.FirstOrDefault(u => u.Email.ToLower() == csvUser.AssignedToEmail.ToLower())
                    : null;

                // Validate that the assigned manager is actually a line manager
                if (csvManager != null)
                {
                    var isLineManager = await _userRepository.IsUserLineManagerAsync(csvManager.Id);
                    if (!isLineManager)
                    {
                        // Skip this user or log a warning - the assigned user is not a line manager
                        csvManager = null; // Ignore invalid manager assignment
                    }
                }

                var csvUserData = new CsvUserDataDTO
                {
                    FirstName = csvUser.FirstName,
                    LastName = csvUser.LastName,
                    Email = csvUser.Email,
                    DepartmentName = csvUser.DepartmentName,
                    AssignedToEmail = csvUser.AssignedToEmail,
                    AssignedToName = csvManager != null ? $"{csvManager.FirstName} {csvManager.LastName}" : null
                };

                // Detect conflicts
                var conflicts = new List<FieldConflictDTO>();

                if (dbUser.FirstName != csvUser.FirstName)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "firstName",
                        DbValue = dbUser.FirstName,
                        CsvValue = csvUser.FirstName,
                        Selected = false
                    });
                }

                if (dbUser.LastName != csvUser.LastName)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "lastName",
                        DbValue = dbUser.LastName,
                        CsvValue = csvUser.LastName,
                        Selected = false
                    });
                }

                if (dbUser.Department.Name != csvUser.DepartmentName)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "departmentName",
                        DbValue = dbUser.Department.Name,
                        CsvValue = csvUser.DepartmentName,
                        Selected = false
                    });
                }

                // Check line manager
                var csvManagerEmail = csvUser.AssignedToEmail?.ToLower();
                var dbManagerId = dbUser.AssignedToId;

                if ((csvManager?.Id ?? null) != dbManagerId)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "assignedToName",
                        DbValue = dbUser.AssignedTo != null ? $"{dbUser.AssignedTo.FirstName} {dbUser.AssignedTo.LastName}" : null,
                        CsvValue = csvManager != null ? $"{csvManager.FirstName} {csvManager.LastName}" : null,
                        Selected = false
                    });
                }

                var comparison = new UserComparisonDTO
                {
                    Id = dbUser.Id.ToString(), // Use actual database user ID
                    Status = conflicts.Count > 0 ? "modified" : "unchanged",
                    DbUser = MapToUserGETResponseDTO(dbUser, dbUsers),
                    CsvUser = csvUserData,
                    Conflicts = conflicts,
                    Selected = conflicts.Count > 0 // Auto-select modified records
                };

                comparisons.Add(comparison);
            }
            else
            {
                // New user from CSV
                var newCsvManager = csvUser.AssignedToEmail != null
                    ? dbUsers.FirstOrDefault(u => u.Email.ToLower() == csvUser.AssignedToEmail.ToLower())
                    : null;

                // Validate that the assigned manager is actually a line manager
                if (newCsvManager != null)
                {
                    var isLineManager = await _userRepository.IsUserLineManagerAsync(newCsvManager.Id);
                    if (!isLineManager)
                    {
                        // Skip this user or log a warning - the assigned user is not a line manager
                        newCsvManager = null; // Ignore invalid manager assignment
                    }
                }

                var comparison = new UserComparisonDTO
                {
                    Id = Guid.NewGuid().ToString(), // For new users, generate new ID
                    Status = "new",
                    CsvUser = new CsvUserDataDTO
                    {
                        FirstName = csvUser.FirstName,
                        LastName = csvUser.LastName,
                        Email = csvUser.Email,
                        DepartmentName = csvUser.DepartmentName,
                        AssignedToEmail = csvUser.AssignedToEmail,
                        AssignedToName = newCsvManager != null ? $"{newCsvManager.FirstName} {newCsvManager.LastName}" : null
                    },
                    Selected = true // Auto-select new records
                };

                comparisons.Add(comparison);
            }
        }

        // Find deleted users (in DB but not in CSV)
        var csvEmails = csvUsers.Select(u => u.Email.ToLower()).ToHashSet();
        foreach (var dbUser in dbUsers)
        {
            if (!csvEmails.Contains(dbUser.Email.ToLower()))
            {
                comparisons.Add(new UserComparisonDTO
                {
                    Id = dbUser.Id.ToString(), // Use actual database user ID for deleted users
                    Status = "deleted",
                    DbUser = MapToUserGETResponseDTO(dbUser, dbUsers),
                    Selected = false // Don't auto-select deletions
                });
            }
        }

        return comparisons;
    }

    public async Task<SyncResultDTO> SyncUsers(SyncRequestDTO syncRequest)
    {
        var result = new SyncResultDTO { Success = true };
        var dbUsers = (await _userRepository.GetAllUsersAsync()).ToList();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        foreach (var item in syncRequest.Items)
        {
            try
            {
                if (item.Status == "new" && item.CsvData != null)
                {
                    // Create new user
                    var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
                    if (department == null)
                    {
                        // Create department if it doesn't exist
                        department = new Department
                        {
                            Id = Guid.NewGuid(),
                            Name = item.CsvData.DepartmentName,
                            CreatedAt = DateTime.UtcNow
                        };
                        await _departmentRepository.AddDepartmentAsync(department);
                        departments.Add(department); // Add to cache
                    }

                    var assignedToId = item.CsvData.AssignedToEmail != null
                        ? dbUsers.FirstOrDefault(u => u.Email.Equals(item.CsvData.AssignedToEmail, StringComparison.OrdinalIgnoreCase))?.Id
                        : null;

                    // Validate that the assigned manager is actually a line manager
                    if (assignedToId.HasValue)
                    {
                        var isLineManager = await _userRepository.IsUserLineManagerAsync(assignedToId.Value);
                        if (!isLineManager)
                        {
                            // Don't assign invalid line manager
                            assignedToId = null;
                        }
                    }

                    var newUser = new User
                    {
                        Id = Guid.NewGuid(),
                        FirstName = item.CsvData.FirstName,
                        LastName = item.CsvData.LastName,
                        Email = item.CsvData.Email,
                        DepartmentId = department.Id,
                        AssignedToId = assignedToId,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _userRepository.AddUserAsync(newUser);
                    dbUsers.Add(newUser); // Add to cache for subsequent operations
                    result.RecordsProcessed++;
                }
                else if (item.Status == "modified" && item.CsvData != null)
                {
                    // Update existing user - reload from database to ensure fresh instance
                    var existingUser = await _userRepository.GetUserByIdAsync(Guid.Parse(item.Id));
                    
                    if (existingUser != null)
                    {
                        bool hasChanges = false;
        
                        // If there are selected conflicts, apply only those resolutions
                        if (item.Conflicts.Any(c => c.Selected))
                        {
                            // Apply only selected conflict resolutions
                            foreach (var conflict in item.Conflicts.Where(c => c.Selected))
                            {
                                // If no SelectedValue is specified, default to "csv"
                                var selectedValue = conflict.SelectedValue ?? "csv";
                
                                if (selectedValue == "csv")
                                {
                                    switch (conflict.Field.ToLower())
                                    {
                                        case "firstname":
                                            if (existingUser.FirstName != item.CsvData.FirstName)
                                            {
                                                existingUser.FirstName = item.CsvData.FirstName;
                                                hasChanges = true;
                                            }
                                            break;
                                        case "lastname":
                                            if (existingUser.LastName != item.CsvData.LastName)
                                            {
                                                existingUser.LastName = item.CsvData.LastName;
                                                hasChanges = true;
                                            }
                                            break;
                                        case "departmentname":
                                            var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
                                            if (department == null)
                                            {
                                                department = new Department
                                                {
                                                    Id = Guid.NewGuid(),
                                                    Name = item.CsvData.DepartmentName,
                                                    CreatedAt = DateTime.UtcNow
                                                };
                                                await _departmentRepository.AddDepartmentAsync(department);
                                                departments.Add(department);
                                            }
                                            if (existingUser.DepartmentId != department.Id)
                                            {
                                                existingUser.DepartmentId = department.Id;
                                                hasChanges = true;
                                            }
                                            break;
                                        case "assignedtoname":
                                            var newAssignedToId = item.CsvData.AssignedToEmail != null
                                                ? dbUsers.FirstOrDefault(u => u.Email.Equals(item.CsvData.AssignedToEmail, StringComparison.OrdinalIgnoreCase))?.Id
                                                : null;
                                            
                                            // Validate that the assigned manager is actually a line manager
                                            if (newAssignedToId.HasValue)
                                            {
                                                var isLineManager = await _userRepository.IsUserLineManagerAsync(newAssignedToId.Value);
                                                if (!isLineManager)
                                                {
                                                    newAssignedToId = null;
                                                }
                                            }
                                            
                                            if (existingUser.AssignedToId != newAssignedToId)
                                            {
                                                existingUser.AssignedToId = newAssignedToId;
                                                hasChanges = true;
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // If no conflicts are selected, update all fields that differ from database
                            if (existingUser.FirstName != item.CsvData.FirstName)
                            {
                                existingUser.FirstName = item.CsvData.FirstName;
                                hasChanges = true;
                            }
                            if (existingUser.LastName != item.CsvData.LastName)
                            {
                                existingUser.LastName = item.CsvData.LastName;
                                hasChanges = true;
                            }
            
                            var dept = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
                            if (dept == null)
                            {
                                dept = new Department
                                {
                                    Id = Guid.NewGuid(),
                                    Name = item.CsvData.DepartmentName,
                                    CreatedAt = DateTime.UtcNow
                                };
                                await _departmentRepository.AddDepartmentAsync(dept);
                                departments.Add(dept);
                            }
                            if (existingUser.DepartmentId != dept.Id)
                            {
                                existingUser.DepartmentId = dept.Id;
                                hasChanges = true;
                            }
            
                            var assignedToId = item.CsvData.AssignedToEmail != null
                                ? dbUsers.FirstOrDefault(u => u.Email.Equals(item.CsvData.AssignedToEmail, StringComparison.OrdinalIgnoreCase))?.Id
                                : null;
                            
                            // Validate that the assigned manager is actually a line manager
                            if (assignedToId.HasValue)
                            {
                                var isLineManager = await _userRepository.IsUserLineManagerAsync(assignedToId.Value);
                                if (!isLineManager)
                                {
                                    assignedToId = null;
                                }
                            }
                            
                            if (existingUser.AssignedToId != assignedToId)
                            {
                                existingUser.AssignedToId = assignedToId;
                                hasChanges = true;
                            }
                        }

                        if (hasChanges)
                        {
                            existingUser.UpdatedAt = DateTime.UtcNow;
                            await _userRepository.UpdateUserAsync(existingUser);
                            result.RecordsProcessed++;
                        }
                        else
                        {
                            result.RecordsSkipped++;
                        }
                    }
                }
                else if (item.Status == "deleted")
                {
                    // Soft delete user if he hasn't been updated in 90 days
                    var userToDelete = await _userRepository.GetUserByIdAsync(Guid.Parse(item.Id));
                    if (userToDelete != null)
                    {
                        if(userToDelete.UpdatedAt != null && userToDelete.UpdatedAt > DateTime.UtcNow.AddDays(-90))
                        {
                            result.RecordsSkipped++;
                            continue; // Skip deletion
                        }

                        userToDelete.DeletedAt = DateTime.UtcNow;
                        await _userRepository.UpdateUserAsync(userToDelete);
                        result.RecordsProcessed++;
                    }
                }
                else
                {
                    result.RecordsSkipped++;
                }
            }
            catch (Exception ex)
            {
                result.RecordsFailed++;
                result.Errors.Add($"Failed to process user {item.CsvData?.Email ?? item.Id}: {ex.Message}");
            }
        }

        result.Success = result.RecordsFailed == 0;
        result.Message = result.Success
            ? $"Successfully synced {result.RecordsProcessed} records"
            : $"Synced with errors: {result.RecordsFailed} failed";

        return result;
    }

    private UserGETResponseDTO MapToUserGETResponseDTO(User user, List<User> allUsers)
    {
        return new UserGETResponseDTO
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department.Name,
            AssignedToId = user.AssignedToId,
            AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<List<CSVDepartmentComparisionDTO>> CompareDepartmentsWithDatabase(List<CSVDepartmentDTO> csvDepartments)
    {
        var comparisons = new List<CSVDepartmentComparisionDTO>();
        var dbDepartments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        var dbDepartmentMap = dbDepartments.ToDictionary(d => d.Name.ToLower(), d => d);

        foreach (var csvDept in csvDepartments)
        {
            var deptName = csvDept.Name.ToLower();

            if (dbDepartmentMap.TryGetValue(deptName, out var dbDept))
            {
                var status = dbDept.Name != csvDept.Name ? "modified" : "deleted";

                comparisons.Add(new CSVDepartmentComparisionDTO
                {
                    CsvDepartment = csvDept,
                    DbDepartment = new DepartmentGETResponseDTO
                    {
                        Id = dbDept.Id,
                        Name = dbDept.Name
                    },
                    Status = status
                });
            }
            else
            {
                comparisons.Add(new CSVDepartmentComparisionDTO
                {
                    CsvDepartment = csvDept,
                    DbDepartment = null,
                    Status = "new"
                });
            }
        }

        var csvDepartmentNames = csvDepartments.Select(d => d.Name.ToLower()).ToHashSet();
        foreach (var dbDept in dbDepartments)
        {
            if (!csvDepartmentNames.Contains(dbDept.Name.ToLower()))
            {
                comparisons.Add(new CSVDepartmentComparisionDTO
                {
                    CsvDepartment = null,
                    DbDepartment = new DepartmentGETResponseDTO
                    {
                        Id = dbDept.Id,
                        Name = dbDept.Name
                    },
                    Status = "deleted"
                });
            }
        }

        return comparisons;
    }

    public async Task<SyncResultDTO> SyncDepartments(List<CSVDepartmentComparisionDTO> departmentSyncList)
    {
        var result = new SyncResultDTO { Success = true };
        var dbDepartments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        foreach (var item in departmentSyncList)
        {
            try
            {
                if (item.Status == "new" && item.CsvDepartment != null)
                {
                    var newDepartment = new Department
                    {
                        Id = Guid.NewGuid(),
                        Name = item.CsvDepartment.Name,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _departmentRepository.AddDepartmentAsync(newDepartment);
                    result.RecordsProcessed++;
                }
                else if (item.Status == "modified" && item.CsvDepartment != null && item.DbDepartment != null)
                {
                    var existingDepartment = await _departmentRepository.GetDepartmentByIdAsync(item.DbDepartment.Id);
                    
                    if (existingDepartment != null && existingDepartment.Name != item.CsvDepartment.Name)
                    {
                        existingDepartment.Name = item.CsvDepartment.Name;
                        existingDepartment.UpdatedAt = DateTime.UtcNow;
                        await _departmentRepository.UpdateDepartmentAsync(existingDepartment);
                        result.RecordsProcessed++;
                    }
                    else
                    {
                        result.RecordsSkipped++;
                    }
                }
                else if (item.Status == "deleted" && item.DbDepartment != null)
                {
                    var usersInDepartment = await _userRepository.GetUsersByDepartmentIdAsync(item.DbDepartment.Id);
                    
                    if (!usersInDepartment.Any())
                    {
                        var departmentToDelete = await _departmentRepository.GetDepartmentByIdAsync(item.DbDepartment.Id);
                        if(departmentToDelete != null)
                        {
                            if (departmentToDelete.UpdatedAt != null && departmentToDelete.UpdatedAt > DateTime.UtcNow.AddDays(-90))
                            {
                                result.RecordsSkipped++;
                                continue;
                            }
                            departmentToDelete.DeletedAt = DateTime.UtcNow;
                            await _departmentRepository.UpdateDepartmentAsync(departmentToDelete);
                            result.RecordsProcessed++;
                        }
                    }
                    else
                    {
                        result.RecordsSkipped++;
                        result.Errors.Add($"Department '{item.DbDepartment.Name}' cannot be deleted because it has assigned users.");
                    }
                }
                else
                {
                    result.RecordsSkipped++;
                }
            }
            catch (Exception ex)
            {
                result.RecordsFailed++;
                result.Errors.Add($"Failed to process department {item.CsvDepartment?.Name ?? item.DbDepartment?.Name ?? "Unknown"}: {ex.Message}");
            }
        }

        result.Success = result.RecordsFailed == 0;
        result.Message = result.Success
            ? $"Successfully synced {result.RecordsProcessed} departments"
            : $"Synced with errors: {result.RecordsFailed} failed";

        return result;
    }
}
