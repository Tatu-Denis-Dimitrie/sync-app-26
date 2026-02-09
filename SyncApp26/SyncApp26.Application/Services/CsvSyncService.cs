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
    private readonly ISyncNotificationService _notificationService;
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IImportHistoryRepository _importHistoryRepository;
    private readonly IImportConflictRepository _importConflictRepository;

    public CsvSyncService(IUserRepository userRepository, IDepartmentRepository departmentRepository, ISyncNotificationService notificationService, IImportHistoryRepository importHistoryRepository, IImportConflictRepository importConflictRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _notificationService = notificationService;
        _importHistoryRepository = importHistoryRepository;
        _importConflictRepository = importConflictRepository;
    }

    public async Task<List<UserComparisonDTO>> CompareWithDatabase(IEnumerable<CsvUserDTO> csvUsers, int totalRows, string? connectionId = null)
    {
        var comparisons = new List<UserComparisonDTO>();

        // Use optimized no-tracking query for read-only comparison
        var dbUsers = await _userRepository.GetAllUsersForComparisonAsync();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();

        // Create a map of email to DB user for O(1) lookup
        var dbUserMap = dbUsers.ToDictionary(u => u.Email.ToLower(), u => u);

        int processedCount = 0;
        int lastPercent = 0;
        var csvEmails = new HashSet<string>();

        // For SignalR progress - don't await to avoid blocking the processing loop
        Task? progressTask = null;

        // Process CSV users
        foreach (var csvUser in csvUsers)
        {
            processedCount++;
            var email = csvUser.Email.ToLower();
            csvEmails.Add(email);

            // Send progress update every 5% or every 500 records (less frequent to reduce overhead)
            if (connectionId != null && processedCount % 500 == 0)
            {
                int currentPercent = totalRows > 0 ? (int)((double)processedCount / totalRows * 100) : 0;
                if (currentPercent > lastPercent || totalRows == 0)
                {
                    lastPercent = currentPercent;
                    // Fire and forget - don't await to avoid blocking
                    progressTask = _notificationService.SendProgress(connectionId, $"Analyzing record {processedCount}...", currentPercent);
                }
            }

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
                    FirstName = csvUser.FirstName.Trim(),
                    LastName = csvUser.LastName.Trim(),
                    Email = csvUser.Email.Trim(),
                    DepartmentName = csvUser.DepartmentName.Trim(),
                    AssignedToEmail = csvUser.AssignedToEmail?.Trim(),
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

                // Stream result to frontend
                if (connectionId != null)
                {
                    await _notificationService.SendComparison(connectionId, comparison);
                }
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
                        FirstName = csvUser.FirstName.Trim(),
                        LastName = csvUser.LastName.Trim(),
                        Email = csvUser.Email.Trim(),
                        DepartmentName = csvUser.DepartmentName.Trim(),
                        AssignedToEmail = csvUser.AssignedToEmail?.Trim(),
                        AssignedToName = newCsvManager != null ? $"{newCsvManager.FirstName} {newCsvManager.LastName}" : null
                    },
                    Selected = true // Auto-select new records
                };

                comparisons.Add(comparison);

                // Stream result to frontend - fire and forget
                if (connectionId != null)
                {
                    _ = _notificationService.SendComparison(connectionId, comparison);
                }
            }
        }

        // Await final progress task if any
        if (progressTask != null)
        {
            await progressTask;
        }

        // Find deleted users (in DB but not in CSV)
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

    public async Task<SyncResultDTO> SyncUsers(SyncRequestDTO syncRequest, string? connectionId = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new SyncResultDTO { Success = true };

        // Load all data once
        var dbUsers = (await _userRepository.GetAllUsersAsync()).ToList();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync()).ToList();
        var dbUserMap = dbUsers.ToDictionary(u => u.Id.ToString(), u => u);

        // Batch collections for bulk operations
        var usersToAdd = new List<User>();
        var usersToUpdate = new List<User>();
        var usersToDelete = new List<User>();
        var departmentsToAdd = new Dictionary<string, Department>();

        // Create import history record
        var importHistory = new ImportHistory
        {
            Id = Guid.NewGuid(),
            ImportDate = DateTime.UtcNow,
            FileName = "CSV Import"
        };
        bool importHistoryCreated = false;

        int totalItems = syncRequest.Items.Count;
        int processedItems = 0;
        Task? progressTask = null;

        // First pass: Prepare all operations (no DB calls)
        foreach (var item in syncRequest.Items)
        {
            processedItems++;

            // Send progress update every 100 items - fire and forget
            if (connectionId != null && processedItems % 100 == 0)
            {
                progressTask = _notificationService.SendSyncProgress(connectionId, result.RecordsProcessed, result.RecordsFailed, result.RecordsSkipped);
            }

            try
            {
                if (item.Status == "new" && item.CsvData != null)
                {
                    // Prepare new user
                    var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase))
                        ?? departmentsToAdd.GetValueOrDefault(item.CsvData.DepartmentName.ToLower());

                    if (department == null)
                    {
                        // Queue department creation
                        department = new Department
                        {
                            Id = Guid.NewGuid(),
                            Name = item.CsvData.DepartmentName.Trim(),
                            CreatedAt = DateTime.UtcNow
                        };
                        departmentsToAdd[item.CsvData.DepartmentName.ToLower()] = department;
                        departments.Add(department);
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
                        FirstName = item.CsvData.FirstName.Trim(),
                        LastName = item.CsvData.LastName.Trim(),
                        Email = item.CsvData.Email.Trim(),
                        DepartmentId = department.Id,
                        AssignedToId = assignedToId,
                        CreatedAt = DateTime.UtcNow
                    };

                    usersToAdd.Add(newUser);
                    dbUsers.Add(newUser); // Add to cache for subsequent lookups
                    result.RecordsProcessed++;
                }
                else if (item.Status == "modified" && item.CsvData != null)
                {
                    // Get existing user from cache (already loaded)
                    if (!dbUserMap.TryGetValue(item.Id, out var existingUser))
                    {
                        result.RecordsFailed++;
                        result.Errors.Add($"User {item.CsvData.Email} not found");
                        continue;
                    }

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

                                if (historyField == "firstname" || historyField == "lastname")
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
                                                    NewValue = item.CsvData.FirstName.Trim(),
                                                    Status = "accepted"
                                                };

                                                existingUser.FirstName = item.CsvData.FirstName.Trim();
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
                                                    NewValue = item.CsvData.LastName.Trim(),
                                                    Status = "accepted"
                                                };
                                                existingUser.LastName = item.CsvData.LastName.Trim();
                                                hasChanges = true;
                                                await _importConflictRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "departmentname":
                                            var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase))
                                                ?? departmentsToAdd.GetValueOrDefault(item.CsvData.DepartmentName.ToLower());
                                            if (department == null)
                                            {
                                                department = new Department
                                                {
                                                    Id = Guid.NewGuid(),
                                                    Name = item.CsvData.DepartmentName.Trim(),
                                                    CreatedAt = DateTime.UtcNow
                                                };
                                                departmentsToAdd[item.CsvData.DepartmentName.ToLower()] = department;
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

                                            if (!newAssignedToId.HasValue)
                                            {
                                                var csvManagerName = conflict.CsvValue?.ToString();
                                                if (!string.IsNullOrWhiteSpace(csvManagerName))
                                                {
                                                    var matchedManager = dbUsers.FirstOrDefault(u =>
                                                        string.Equals($"{u.FirstName} {u.LastName}", csvManagerName, StringComparison.OrdinalIgnoreCase));
                                                    if (matchedManager != null)
                                                    {
                                                        newAssignedToId = matchedManager.Id;
                                                    }
                                                }
                                            }

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
                                                var importConflict = new ImportConflict
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "assignedtoname",
                                                    OldValue = existingUser.AssignedTo != null ? $"{existingUser.AssignedTo.FirstName} {existingUser.AssignedTo.LastName}" : string.Empty,
                                                    NewValue = item.CsvData.AssignedToEmail ?? string.Empty,
                                                    Status = "accepted"
                                                };
                                                existingUser.AssignedToId = newAssignedToId;
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
                                existingUser.FirstName = item.CsvData.FirstName.Trim();
                                hasChanges = true;
                            }
                            if (existingUser.LastName != item.CsvData.LastName)
                            {
                                existingUser.LastName = item.CsvData.LastName.Trim();
                                hasChanges = true;
                            }

                            var dept = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase))
                                ?? departmentsToAdd.GetValueOrDefault(item.CsvData.DepartmentName.ToLower());
                            if (dept == null)
                            {
                                dept = new Department
                                {
                                    Id = Guid.NewGuid(),
                                    Name = item.CsvData.DepartmentName.Trim(),
                                    CreatedAt = DateTime.UtcNow
                                };
                                departmentsToAdd[item.CsvData.DepartmentName.ToLower()] = dept;
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
                            usersToUpdate.Add(existingUser);
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
                    // Soft delete user if hasn't been updated in 90 days
                    if (dbUserMap.TryGetValue(item.Id, out var userToDelete))
                    {
                        if (userToDelete.UpdatedAt != null && userToDelete.UpdatedAt > DateTime.UtcNow.AddDays(-90))
                        {
                            result.RecordsSkipped++;
                            continue; // Skip deletion
                        }

                        userToDelete.DeletedAt = DateTime.UtcNow;
                        usersToDelete.Add(userToDelete);
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

        // Await final progress task if any
        if (progressTask != null)
        {
            await progressTask;
        }

        // Execute all batched operations
        try
        {
            // Add new departments first (referenced by users)
            if (departmentsToAdd.Any())
            {
                foreach (var dept in departmentsToAdd.Values)
                {
                    await _departmentRepository.AddDepartmentAsync(dept);
                }
            }

            // Bulk add new users
            if (usersToAdd.Any())
            {
                await _userRepository.AddUsersAsync(usersToAdd);
            }

            // Bulk update modified users
            if (usersToUpdate.Any())
            {
                await _userRepository.UpdateUsersAsync(usersToUpdate);
            }

            // Bulk update deleted users (soft delete)
            if (usersToDelete.Any())
            {
                await _userRepository.UpdateUsersAsync(usersToDelete);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Failed to execute batch operations: {ex.Message}");
        }

        // Final status update
        if (connectionId != null)
        {
            await _notificationService.SendSyncProgress(connectionId, result.RecordsProcessed, result.RecordsFailed, result.RecordsSkipped);
        }

        stopwatch.Stop();
        result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
        result.Success = result.RecordsFailed == 0;
        result.Message = result.Success
            ? $"Successfully synced {result.RecordsProcessed} records in {result.ProcessingTimeMs}ms"
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
