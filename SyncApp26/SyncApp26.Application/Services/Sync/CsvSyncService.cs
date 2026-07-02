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
    private readonly IFunctionRepository _functionRepository;
    private readonly IImportHistoryRepository _importHistoryRepository;
    private readonly IUserChangeHistoryRepository _userChangeHistoryRepository;

    public CsvSyncService(IUserRepository userRepository, IDepartmentRepository departmentRepository, IFunctionRepository functionRepository, ISyncNotificationService notificationService, IImportHistoryRepository importHistoryRepository, IUserChangeHistoryRepository userChangeHistoryRepositoryRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _functionRepository = functionRepository;
        _notificationService = notificationService;
        _importHistoryRepository = importHistoryRepository;
        _userChangeHistoryRepository = userChangeHistoryRepositoryRepository;
    }

    public async Task<List<UserComparisonDTO>> CompareWithDatabase(IEnumerable<CsvUserDTO> csvUsers, int totalRows, string? connectionId = null)
    {
        var comparisons = new List<UserComparisonDTO>();

        // Use optimized no-tracking query for read-only comparison
        var dbUsers = await _userRepository.GetAllUsersForComparisonAsync();
        var departments = (await _departmentRepository.GetAllDepartmentsAsync())
            .Where(d => d.IsActive) // Only consider active departments
            .ToList();

        // Create a map of personalId to DB user for quick lookup
        var dbUserMap = dbUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.PersonalId))
            .ToDictionary(u => u.PersonalId.Trim(), u => u, StringComparer.OrdinalIgnoreCase);

        var csvEmails = new HashSet<string>();

        // Process CSV users
        foreach (var csvUser in csvUsers)
        {
            if (string.IsNullOrWhiteSpace(csvUser.PersonalId))
            {
                continue;
            }

            var personalId = csvUser.PersonalId.Trim();

            if (dbUserMap.TryGetValue(personalId, out var dbUser))
            {
                // User exists - compare fields
                var csvManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvUser.AssignedToPersonalId);

                var csvUserData = new CsvUserDataDTO
                {
                    PersonalId = csvUser.PersonalId,
                    FirstName = csvUser.FirstName,
                    LastName = csvUser.LastName,
                    Email = csvUser.Email,
                    DepartmentName = csvUser.DepartmentName,
                    AssignedToPersonalId = csvUser.AssignedToPersonalId,
                    AssignedToName = csvManager != null ? $"{csvManager.FirstName} {csvManager.LastName}" : null,
                    Function = csvUser.Function != null ? csvUser.Function.Trim() : null
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

                if (dbUser.Department?.Name != csvUser.DepartmentName)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "departmentName",
                        DbValue = dbUser.Department?.Name ?? string.Empty,
                        CsvValue = csvUser.DepartmentName,
                        Selected = false
                    });
                }

                if (dbUser.Email != csvUser.Email)
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "email",
                        DbValue = dbUser.Email,
                        CsvValue = csvUser.Email,
                        Selected = false
                    });
                }

                var dbFunctionName = dbUser.Function?.Name?.Trim();
                var csvFunctionName = csvUser.Function?.Trim();
                if (!string.Equals(dbFunctionName, csvFunctionName, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(new FieldConflictDTO
                    {
                        Field = "function",
                        DbValue = dbFunctionName ?? string.Empty,
                        CsvValue = csvFunctionName,
                        Selected = false
                    });
                }

                // Check line manager
                var csvManagerId = csvManager?.Id;
                var dbManagerId = dbUser.AssignedToId;

                if (csvManagerId != dbManagerId)
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
                var newCsvManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvUser.AssignedToPersonalId);

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
                        AssignedToName = newCsvManager != null ? $"{newCsvManager.FirstName} {newCsvManager.LastName}" : null,
                        Function = csvUser.Function != null ? csvUser.Function.Trim() : null
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

        // Find deleted users (in DB but not in CSV) by PersonalId
        var csvPersonalIds = csvUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.PersonalId))
            .Select(u => u.PersonalId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dbUser in dbUsers)
        {
            if (string.IsNullOrWhiteSpace(dbUser.PersonalId) || !csvPersonalIds.Contains(dbUser.PersonalId.Trim()))
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
        var departments = (await _departmentRepository.GetAllDepartmentsAsync())
            .Where(d => d.IsActive) // Only consider active departments
            .ToList();
        var dbUserMap = dbUsers.ToDictionary(u => u.Id.ToString(), u => u);
        var functionCache = new Dictionary<string, Function?>(StringComparer.OrdinalIgnoreCase);
        var lineManagerRoleId = await _userRepository.GetRoleIdByNameAsync("Line Manager");
        var basicUserRoleId = await _userRepository.GetRoleIdByNameAsync("Basic User");

        // Batch collections for bulk operations
        var usersToAdd = new List<User>();
        var usersToUpdate = new List<User>();
        var usersToDelete = new List<User>();

        // Create import history record
        var importHistory = new ImportHistory
        {
            Id = Guid.NewGuid(),
            ImportDate = DateTime.UtcNow,
            FileName = syncRequest.FileName ?? "CSV Import"
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
                    var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));

                    if (department == null)
                    {
                        // Department does not exist or is inactive - cannot create user
                        result.RecordsFailed++;
                        result.Errors.Add($"User {item.CsvData.Email}: Department '{item.CsvData.DepartmentName}' does not exist or is inactive. Please ensure the department exists and is active.");
                        continue;
                    }

                    var assignedManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, item.CsvData.AssignedToPersonalId);
                    var csvFunction = await ResolveExistingFunctionAsync(item.CsvData.Function, functionCache);

                    if (basicUserRoleId == null)
                    {
                        result.RecordsFailed++;
                        result.Errors.Add($"User {item.CsvData.Email}: Required role not found for creation.");
                        continue;
                    }

                    var newUser = new User
                    {
                        Id = Guid.NewGuid(),
                        RoleId = basicUserRoleId.Value, // Everyone starts as Basic User
                        FirstName = item.CsvData.FirstName.Trim(),
                        LastName = item.CsvData.LastName.Trim(),
                        Email = item.CsvData.Email.Trim(),
                        DepartmentId = department.Id,
                        AssignedToId = assignedManager?.Id,
                        PersonalId = item.CsvData.PersonalId,
                        FunctionId = csvFunction?.Id,
                        Function = csvFunction,
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

                                var rejectedConflict = new UserChangeHistory
                                {
                                    Id = Guid.NewGuid(),
                                    ImportHistoryId = importHistory.Id,
                                    UserId = existingUser.Id,
                                    FieldName = historyField,
                                    OldValue = conflict.DbValue?.ToString() ?? string.Empty,
                                    NewValue = conflict.CsvValue?.ToString() ?? string.Empty,
                                    Status = "rejected"
                                };

                                await _userChangeHistoryRepository.AddAsync(rejectedConflict);
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
                                                var importConflict = new UserChangeHistory
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
                                                await _userChangeHistoryRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "lastname":
                                            if (existingUser.LastName != item.CsvData.LastName)
                                            {
                                                var importConflict = new UserChangeHistory
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
                                                await _userChangeHistoryRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "email":
                                            if (existingUser.Email != item.CsvData.Email)
                                            {
                                                var importConflict = new UserChangeHistory
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
                                                await _userChangeHistoryRepository.AddAsync(importConflict);
                                            }
                                            break;
                                        case "departmentname":
                                            var department = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
                                            if (department == null)
                                            {
                                                // Department does not exist or is inactive - skip this field update
                                                result.Errors.Add($"User {item.CsvData.Email}: Cannot update department to '{item.CsvData.DepartmentName}' - department does not exist or is inactive.");
                                                break;
                                            }
                                            if (existingUser.DepartmentId != department.Id)
                                            {
                                                var userChangeHistory = new UserChangeHistory
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "departmentname",
                                                    OldValue = existingUser.Department?.Name ?? string.Empty,
                                                    NewValue = department.Name,
                                                    Status = "accepted"
                                                };
                                                existingUser.DepartmentId = department.Id;
                                                hasChanges = true;
                                                await _userChangeHistoryRepository.AddAsync(userChangeHistory);
                                            }
                                            break;
                                        case "assignedtoname":
                                            var newAssignedTo = await ResolveLineManagerByPersonalIdAsync(dbUsers, item.CsvData.AssignedToPersonalId);
                                            var newAssignedToId = newAssignedTo?.Id;

                                            if (existingUser.AssignedToId != newAssignedToId)
                                            {
                                                var userChangeHistory = new UserChangeHistory
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "assignedtoname",
                                                    OldValue = existingUser.AssignedTo != null ? $"{existingUser.AssignedTo.FirstName} {existingUser.AssignedTo.LastName}" : string.Empty,
                                                    NewValue = newAssignedTo != null ? $"{newAssignedTo.FirstName} {newAssignedTo.LastName}" : string.Empty,
                                                    Status = "accepted"
                                                };
                                                existingUser.AssignedToId = newAssignedToId;
                                                hasChanges = true;
                                                await _userChangeHistoryRepository.AddAsync(userChangeHistory);
                                            }
                                            break;
                                        case "function":
                                            var selectedCsvFunction = await ResolveExistingFunctionAsync(item.CsvData.Function, functionCache);
                                            var selectedCsvFunctionName = item.CsvData.Function?.Trim();

                                            if (existingUser.FunctionId != selectedCsvFunction?.Id)
                                            {
                                                var userChangeHistory = new UserChangeHistory
                                                {
                                                    Id = Guid.NewGuid(),
                                                    ImportHistoryId = importHistory.Id,
                                                    UserId = existingUser.Id,
                                                    FieldName = "function",
                                                    OldValue = existingUser.Function?.Name ?? string.Empty,
                                                    NewValue = selectedCsvFunctionName ?? string.Empty,
                                                    Status = "accepted"
                                                };
                                                existingUser.FunctionId = selectedCsvFunction?.Id;
                                                existingUser.Function = selectedCsvFunction;
                                                hasChanges = true;
                                                await _userChangeHistoryRepository.AddAsync(userChangeHistory);
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
                            var csvFunction = await ResolveExistingFunctionAsync(item.CsvData.Function, functionCache);
                            if (existingUser.FunctionId != csvFunction?.Id)
                            {
                                existingUser.FunctionId = csvFunction?.Id;
                                existingUser.Function = csvFunction;
                                hasChanges = true;
                            }

                            var dept = departments.FirstOrDefault(d => d.Name.Equals(item.CsvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
                            if (dept == null)
                            {
                                // Department does not exist or is inactive - skip this user update
                                result.RecordsFailed++;
                                result.Errors.Add($"User {item.CsvData.Email}: Department '{item.CsvData.DepartmentName}' does not exist or is inactive. Cannot update user.");
                                continue;
                            }
                            if (existingUser.DepartmentId != dept.Id)
                            {
                                existingUser.DepartmentId = dept.Id;
                                hasChanges = true;
                            }

                            var assignedToManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, item.CsvData.AssignedToPersonalId);
                            var assignedToId = assignedToManager?.Id;

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

            // Promote to Line Manager anyone who is referenced as a manager by another user
            if (lineManagerRoleId != null && basicUserRoleId != null)
            {
                var allUsers = (await _userRepository.GetAllUsersAsync()).ToList();
                var managerIds = allUsers
                    .Where(u => u.AssignedToId.HasValue)
                    .Select(u => u.AssignedToId!.Value)
                    .ToHashSet();

                var usersToPromote = allUsers
                    .Where(u => managerIds.Contains(u.Id) && u.RoleId != lineManagerRoleId.Value)
                    .ToList();
                var usersToDemote = allUsers
                    .Where(u => !managerIds.Contains(u.Id) && u.RoleId == lineManagerRoleId.Value)
                    .ToList();

                foreach (var u in usersToPromote) u.RoleId = lineManagerRoleId.Value;
                foreach (var u in usersToDemote)  u.RoleId = basicUserRoleId.Value;

                var roleUpdates = usersToPromote.Concat(usersToDemote).ToList();
                if (roleUpdates.Any())
                    await _userRepository.UpdateUsersAsync(roleUpdates);
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
            PersonalId = user.PersonalId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            DepartmentId = user.DepartmentId ?? Guid.Empty,
            DepartmentName = user.Department?.Name ?? "No Department",
            AssignedToId = user.AssignedTo?.Id,
            AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
            Function = user.Function?.Name,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    private async Task<Function?> ResolveExistingFunctionAsync(string? functionName, Dictionary<string, Function?> functionCache)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            return null;
        }

        var normalizedName = functionName.Trim();
        if (functionCache.TryGetValue(normalizedName, out var cachedFunction))
        {
            return cachedFunction;
        }

        var existingFunction = await _functionRepository.GetByNameAsync(normalizedName);
        functionCache[normalizedName] = existingFunction;
        return existingFunction;
    }

    private async Task<User?> ResolveLineManagerByPersonalIdAsync(List<User> dbUsers, string? managerPersonalId)
    {
        if (string.IsNullOrWhiteSpace(managerPersonalId))
        {
            return null;
        }

        var manager = dbUsers.FirstOrDefault(u => string.Equals(u.PersonalId, managerPersonalId, StringComparison.OrdinalIgnoreCase));
        if (manager == null)
        {
            return null;
        }

        var isLineManager = await _userRepository.IsUserLineManagerAsync(manager.Id);
        return isLineManager ? manager : null;
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
