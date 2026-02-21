using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs;
using SyncApp26.Shared.DTOs.Response.User;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.CSV.Department;
using SyncApp26.Shared.DTOs.Response.Department;
using SyncApp26.Shared.DTOs.CSV.History;
using System.IO;

namespace SyncApp26.Application.Services;

public class CsvSyncService : ICsvSyncService
{
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IImportHistoryRepository _importHistoryRepository;
    private readonly IImportConflictRepository _importConflictRepository;

    public CsvSyncService(IUserRepository userRepository, IDepartmentRepository departmentRepository, IImportHistoryRepository importHistoryRepository, IImportConflictRepository importConflictRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _importHistoryRepository = importHistoryRepository;
        _importConflictRepository = importConflictRepository;
    }

    public async Task<List<UserComparisonDTO>> CompareWithDatabase(List<CsvUserDTO> csvUsers)
    {
        var comparisons = new List<UserComparisonDTO>();
        var dbUsers = (await _userRepository.GetAllUsersAsync()).ToList();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        // Create a map of email to DB user for quick lookup
        var dbUserMap = dbUsers.ToDictionary(u => u.PersonalId.Trim(), u => u);

        // Process CSV users
        foreach (var csvUser in csvUsers)
        {
            var personalId = csvUser.PersonalId.Trim();

            if (dbUserMap.TryGetValue(personalId, out var dbUser))
            {
                // User exists - compare fields
                var csvManager = csvUser.AssignedToPersonalId != null
                    ? dbUsers.FirstOrDefault(u => u.PersonalId == csvUser.AssignedToPersonalId)
                    : null;

                // Validate that the assigned manager is actually a line manager
                if (csvManager != null)
                {
                    var isLineManager = await _userRepository.IsUserLineManagerAsync(csvManager.PersonalId);
                    if (!isLineManager)
                    {
                        // Skip this user or log a warning - the assigned user is not a line manager
                        csvManager = null; // Ignore invalid manager assignment
                    }
                }

                var csvUserData = new CsvUserDataDTO
                {
                    PersonalId = csvUser.PersonalId,
                    FirstName = csvUser.FirstName,
                    LastName = csvUser.LastName,
                    Email = csvUser.Email,
                    DepartmentName = csvUser.DepartmentName,
                    AssignedToPersonalId = csvUser.AssignedToPersonalId,
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

                if(dbUser.Email != csvUser.Email)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "email",
                        DbValue = dbUser.Email,
                        CsvValue = csvUser.Email,
                        Selected = false
                    });
                }

                // Check line manager
                var csvManagerPersonalId = csvUser.AssignedToPersonalId;
                var dbManagerPersonalId = dbUser.AssignedToPersonalId;

                if (csvManagerPersonalId != dbManagerPersonalId)
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
                var newCsvManager = csvUser.AssignedToPersonalId != null
                    ? dbUsers.FirstOrDefault(u => u.PersonalId == csvUser.AssignedToPersonalId)
                    : null;

                // Validate that the assigned manager is actually a line manager
                if (newCsvManager != null)
                {
                    var isLineManager = await _userRepository.IsUserLineManagerAsync(newCsvManager.PersonalId);
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
                        PersonalId = csvUser.PersonalId,
                        FirstName = csvUser.FirstName,
                        LastName = csvUser.LastName,
                        Email = csvUser.Email,
                        DepartmentName = csvUser.DepartmentName,
                        AssignedToPersonalId = csvUser.AssignedToPersonalId,
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

        var importHistory = new ImportHistory
        {
            Id = Guid.NewGuid(),
            ImportDate = DateTime.UtcNow,
            FileName = string.IsNullOrWhiteSpace(syncRequest.FileName)
                ? $"Import_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
                : Path.GetFileName(syncRequest.FileName)
        };

        bool importHistoryCreated = false;

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

                    var assignedToPersonalId = item.CsvData.AssignedToPersonalId != null
                        ? dbUsers.FirstOrDefault(u => u.PersonalId == item.CsvData.AssignedToPersonalId)?.PersonalId
                        : null;

                    // Validate that the assigned manager is actually a line manager
                    if (assignedToPersonalId != null)
                    {
                        var isLineManager = await _userRepository.IsUserLineManagerAsync(assignedToPersonalId);
                        if (!isLineManager)
                        {
                            // Don't assign invalid line manager
                            assignedToPersonalId = null;
                        }
                    }

                    var newUser = new User
                    {
                        Id = Guid.NewGuid(),
                        FirstName = item.CsvData.FirstName,
                        LastName = item.CsvData.LastName,
                        Email = item.CsvData.Email,
                        DepartmentId = department.Id,
                        AssignedToPersonalId = assignedToPersonalId,
                        PersonalId = Guid.NewGuid().ToString(),
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
                        if (item.Conflicts.Any())
                        {
                            if (!importHistoryCreated)
                            {
                                await _importHistoryRepository.AddAsync(importHistory);
                                importHistoryCreated = true;
                            }

                            foreach (var conflict in item.Conflicts)
                            {
                                var selectedValue = conflict.SelectedValue ?? (conflict.Selected ? "csv" : "db");
                                if (!selectedValue.Equals("db", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                var normalizedField = conflict.Field.Trim().ToLower();
                                var historyField = normalizedField == "assignedtoname" ? "linemanager" : normalizedField;

                                var rejectedConflict = new ImportConflict
                                {
                                    Id = Guid.NewGuid(),
                                    ImportHistoryId = importHistory.Id,
                                    UserId = existingUser.Id,
                                    FieldName = historyField,
                                    OldValue = conflict.DbValue?.ToString() ?? string.Empty,
                                    NewValue = conflict.CsvValue?.ToString() ?? string.Empty,
                                    Status = "rejected"
                                };

                                if(historyField == "firstname" || historyField == "lastname")
                                {
                                    rejectedConflict.Status = "accepted";
                                }

                                await _importConflictRepository.AddAsync(rejectedConflict);
                            }
                        }

                        bool hasChanges = false;
        
                        // If conflicts exist, apply only selected resolutions
                        if (item.Conflicts.Any())
                        {
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
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "firstname",
                                                    OldValue = existingUser.FirstName,
                                                    NewValue = item.CsvData.FirstName,
                                                    Status = "accepted"
                                                };

                                                existingUser.FirstName = item.CsvData.FirstName;
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "lastname":
                                            if (existingUser.LastName != item.CsvData.LastName)
                                            {
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "lastname",
                                                    OldValue = existingUser.LastName,
                                                    NewValue = item.CsvData.LastName,
                                                    Status = "accepted"
                                                };
                                                existingUser.LastName = item.CsvData.LastName;
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "email":
                                            if(existingUser.Email != item.CsvData.Email)
                                            {
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "email",
                                                    OldValue = existingUser.Email,
                                                    NewValue = item.CsvData.Email,
                                                    Status = "accepted"
                                                };
                                                existingUser.Email = item.CsvData.Email;
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
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
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "departmentname",
                                                    OldValue = existingUser.Department.Name,
                                                    NewValue = department.Name,
                                                    Status = "accepted"
                                                }; 
                                                existingUser.DepartmentId = department.Id;
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "assignedtoname":
                                            var newAssignedToPersonalId = item.CsvData.AssignedToPersonalId != null
                                                ? dbUsers.FirstOrDefault(u => u.PersonalId == item.CsvData.AssignedToPersonalId)?.PersonalId
                                                : null;
                                            
                                            // Validate that the assigned manager is actually a line manager
                                            if (newAssignedToPersonalId != null)
                                            {
                                                var isLineManager = await _userRepository.IsUserLineManagerAsync(newAssignedToPersonalId);
                                                if (!isLineManager)
                                                {
                                                    newAssignedToPersonalId = null;
                                                }
                                            }

                                            if (newAssignedToPersonalId == null)
                                            {
                                                var csvManagerName = conflict.CsvValue?.ToString();
                                                if (!string.IsNullOrWhiteSpace(csvManagerName))
                                                {
                                                    var matchedManager = dbUsers.FirstOrDefault(u =>
                                                        string.Equals($"{u.FirstName} {u.LastName}", csvManagerName, StringComparison.OrdinalIgnoreCase));
                                                    if (matchedManager != null)
                                                    {
                                                        newAssignedToPersonalId = matchedManager.PersonalId;
                                                    }
                                                }
                                            }

                                            if (newAssignedToPersonalId != null)
                                            {
                                                var isLineManager = await _userRepository.IsUserLineManagerAsync(newAssignedToPersonalId);
                                                if (!isLineManager)
                                                {
                                                    newAssignedToPersonalId = null;
                                                }
                                            }
                                            
                                            if (existingUser.AssignedToPersonalId != newAssignedToPersonalId)
                                            {
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "assignedtoname",
                                                    OldValue = existingUser.AssignedTo != null ? $"{existingUser.AssignedTo.FirstName} {existingUser.AssignedTo.LastName}" : null,
                                                    NewValue = item.CsvData.AssignedToPersonalId,
                                                    Status = "accepted"
                                                };
                                                existingUser.AssignedToPersonalId = newAssignedToPersonalId;
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // If no conflicts exist, update all fields that differ from database
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
            
                            var assignedToPersonalId = item.CsvData.AssignedToPersonalId != null
                                ? dbUsers.FirstOrDefault(u => u.PersonalId == item.CsvData.AssignedToPersonalId)?.PersonalId
                                : null;
                            
                            // Validate that the assigned manager is actually a line manager
                            if (assignedToPersonalId != null)
                            {
                                var isLineManager = await _userRepository.IsUserLineManagerAsync(assignedToPersonalId);
                                if (!isLineManager)
                                {
                                    assignedToPersonalId = null;
                                }
                            }
                            
                            if (existingUser.AssignedToPersonalId != assignedToPersonalId)
                            {
                                existingUser.AssignedToPersonalId = assignedToPersonalId;
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
            PersonalId = user.PersonalId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department.Name,
            AssignedToPersonalId = user.AssignedToPersonalId,
            AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<List<CSVDepartmentComparisionDTO>> CompareDepartmentsWithDatabase(List<CSVDepartmentDTO> csvDepartments)
    {
        var comparisons = new List<CSVDepartmentComparisionDTO>();
        var dbDepartments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        var dbDepartmentMap = dbDepartments.ToDictionary(d => d.Name.Trim().ToLower(), d => d);

        foreach (var csvDept in csvDepartments)
        {
            var deptName = csvDept.Name.Trim().ToLower();

            if (dbDepartmentMap.TryGetValue(deptName, out var dbDept))
            {
                // Department already exists - mark as unchanged
                comparisons.Add(new CSVDepartmentComparisionDTO
                {
                    CsvDepartment = csvDept,
                    DbDepartment = new DepartmentGETResponseDTO
                    {
                        Id = dbDept.Id,
                        Name = dbDept.Name
                    },
                    Status = "unchanged"
                });
            }
            else
            {
                // New department from CSV
                comparisons.Add(new CSVDepartmentComparisionDTO
                {
                    CsvDepartment = csvDept,
                    DbDepartment = null,
                    Status = "new"
                });
            }
        }

        return comparisons;
    }

    public async Task<SyncResultDTO> SyncDepartments(List<CSVDepartmentComparisionDTO> departmentSyncList)
    {
        var result = new SyncResultDTO { Success = true };

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
                else
                {
                    // Skip unchanged departments
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
