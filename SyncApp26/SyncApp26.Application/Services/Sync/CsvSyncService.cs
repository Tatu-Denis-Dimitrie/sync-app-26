using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Domain.Enums;
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

        // Process CSV users
        foreach (var csvUser in csvUsers)
        {
            if (!IsValidCsvRow(csvUser))
            {
                continue;
            }

            var personalId = csvUser.PersonalId.Trim();

            if (dbUserMap.TryGetValue(personalId, out var dbUser))
            {
                var comparison = await BuildExistingUserComparisonAsync(dbUser, csvUser, dbUsers);
                comparisons.Add(comparison);

                // Stream result to frontend
                if (connectionId != null)
                {
                    await _notificationService.SendComparison(connectionId, comparison);
                }
            }
            else
            {
                var comparison = await BuildNewUserComparisonAsync(csvUser, dbUsers);
                comparisons.Add(comparison);

                // Stream result to frontend - fire and forget
                if (connectionId != null)
                {
                    _ = _notificationService.SendComparison(connectionId, comparison);
                }
            }
        }

        comparisons.AddRange(FindDeletedUserComparisons(dbUsers, csvUsers));

        return comparisons;
    }

    private static bool IsValidCsvRow(CsvUserDTO csvUser)
    {
        return !string.IsNullOrWhiteSpace(csvUser.PersonalId);
    }

    private async Task<UserComparisonDTO> BuildExistingUserComparisonAsync(User dbUser, CsvUserDTO csvUser, List<User> dbUsers)
    {
        // User exists - compare fields
        var csvManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvUser.AssignedToPersonalId);
        var csvUserData = MapToCsvUserDataDTO(csvUser, csvManager);
        var conflicts = DetectFieldConflicts(dbUser, csvUser, csvManager);

        return new UserComparisonDTO
        {
            Id = dbUser.Id.ToString(), // Use actual database user ID
            Status = conflicts.Count > 0 ? "modified" : "unchanged",
            DbUser = MapToUserGETResponseDTO(dbUser, dbUsers),
            CsvUser = csvUserData,
            Conflicts = conflicts,
            Selected = conflicts.Count > 0 // Auto-select modified records
        };
    }

    private async Task<UserComparisonDTO> BuildNewUserComparisonAsync(CsvUserDTO csvUser, List<User> dbUsers)
    {
        // New user from CSV
        var newCsvManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvUser.AssignedToPersonalId);

        return new UserComparisonDTO
        {
            Id = Guid.NewGuid().ToString(), // For new users, generate new ID
            Status = "new",
            CsvUser = MapToCsvUserDataDTO(csvUser, newCsvManager),
            Selected = true // Auto-select new records
        };
    }

    private static CsvUserDataDTO MapToCsvUserDataDTO(CsvUserDTO csvUser, User? manager)
    {
        return new CsvUserDataDTO
        {
            PersonalId = csvUser.PersonalId,
            FirstName = csvUser.FirstName,
            LastName = csvUser.LastName,
            Email = csvUser.Email,
            DepartmentName = csvUser.DepartmentName,
            AssignedToPersonalId = csvUser.AssignedToPersonalId,
            AssignedToName = manager != null ? $"{manager.FirstName} {manager.LastName}" : null,
            Function = csvUser.Function != null ? csvUser.Function.Trim() : null
        };
    }

    private static List<FieldConflictDTO> DetectFieldConflicts(User dbUser, CsvUserDTO csvUser, User? csvManager)
    {
        var conflicts = new List<FieldConflictDTO>();

        AddFieldConflictIfDifferent(conflicts, "firstName", dbUser.FirstName, csvUser.FirstName, dbUser.FirstName != csvUser.FirstName);
        AddFieldConflictIfDifferent(conflicts, "lastName", dbUser.LastName, csvUser.LastName, dbUser.LastName != csvUser.LastName);

        var dbDepartmentName = dbUser.Department?.Name;
        AddFieldConflictIfDifferent(conflicts, "departmentName", dbDepartmentName ?? string.Empty, csvUser.DepartmentName, dbDepartmentName != csvUser.DepartmentName);

        AddFieldConflictIfDifferent(conflicts, "email", dbUser.Email, csvUser.Email, dbUser.Email != csvUser.Email);

        var dbFunctionName = dbUser.Function?.Name?.Trim();
        var csvFunctionName = csvUser.Function?.Trim();
        AddFieldConflictIfDifferent(conflicts, "function", dbFunctionName ?? string.Empty, csvFunctionName,
            !string.Equals(dbFunctionName, csvFunctionName, StringComparison.OrdinalIgnoreCase));

        // Check line manager
        var dbManagerName = dbUser.AssignedTo != null ? $"{dbUser.AssignedTo.FirstName} {dbUser.AssignedTo.LastName}" : null;
        var csvManagerName = csvManager != null ? $"{csvManager.FirstName} {csvManager.LastName}" : null;
        AddFieldConflictIfDifferent(conflicts, "assignedToName", dbManagerName, csvManagerName, csvManager?.Id != dbUser.AssignedToId);

        return conflicts;
    }

    // Adds a field conflict to the list when the DB and CSV values differ.
    private static void AddFieldConflictIfDifferent(List<FieldConflictDTO> conflicts, string field, object? dbValue, object? csvValue, bool valuesDiffer)
    {
        if (!valuesDiffer)
        {
            return;
        }

        conflicts.Add(new FieldConflictDTO
        {
            Field = field,
            DbValue = dbValue,
            CsvValue = csvValue,
            Selected = false
        });
    }

    private List<UserComparisonDTO> FindDeletedUserComparisons(List<User> dbUsers, IEnumerable<CsvUserDTO> csvUsers)
    {
        // Find deleted users (in DB but not in CSV) by PersonalId
        var csvPersonalIds = csvUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.PersonalId))
            .Select(u => u.PersonalId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedComparisons = new List<UserComparisonDTO>();
        foreach (var dbUser in dbUsers)
        {
            if (string.IsNullOrWhiteSpace(dbUser.PersonalId) || !csvPersonalIds.Contains(dbUser.PersonalId.Trim()))
            {
                deletedComparisons.Add(new UserComparisonDTO
                {
                    Id = dbUser.Id.ToString(), // Use actual database user ID for deleted users
                    Status = "deleted",
                    DbUser = MapToUserGETResponseDTO(dbUser, dbUsers),
                    Selected = false // Don't auto-select deletions
                });
            }
        }

        return deletedComparisons;
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
                    var newUser = await TryBuildNewUserAsync(item.CsvData, departments, dbUsers, functionCache, result);
                    if (newUser == null)
                    {
                        continue;
                    }

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
                        var csvData = item.CsvData!;

                        if (item.Conflicts.Any())
                        {
                            if (!importHistoryCreated)
                            {
                                await _importHistoryRepository.AddAsync(importHistory);
                                importHistoryCreated = true;
                            }

                            await RecordRejectedConflictsAsync(item.Conflicts, existingUser, importHistory);
                        }

                        bool hasChanges;

                        // If conflicts exist, apply only selected resolutions; otherwise sync every differing field
                        if (item.Conflicts.Any())
                        {
                            hasChanges = await ApplySelectedConflictResolutionsAsync(item.Conflicts, csvData, existingUser, departments, dbUsers, functionCache, importHistory, result);
                        }
                        else
                        {
                            var (success, changed) = await ApplyAllDifferingFieldsAsync(csvData, existingUser, departments, dbUsers, functionCache, result);
                            if (!success)
                            {
                                continue;
                            }
                            hasChanges = changed;
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
                    ProcessDeletedItem(item, dbUserMap, usersToDelete, result);
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
            {
                var allUsers = (await _userRepository.GetAllUsersAsync()).ToList();
                var managerIds = allUsers
                    .Where(u => u.AssignedToId.HasValue)
                    .Select(u => u.AssignedToId!.Value)
                    .ToHashSet();

                var usersToPromote = allUsers
                    .Where(u => managerIds.Contains(u.Id) && u.Role != UserRole.LineManager)
                    .ToList();
                var usersToDemote = allUsers
                    .Where(u => !managerIds.Contains(u.Id) && u.Role == UserRole.LineManager)
                    .ToList();

                foreach (var u in usersToPromote) u.Role = UserRole.LineManager;
                foreach (var u in usersToDemote)  u.Role = UserRole.BasicUser;

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

    private async Task<User?> TryBuildNewUserAsync(CsvUserDTO csvData, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, SyncResultDTO result)
    {
        var department = departments.FirstOrDefault(d => d.Name.Equals(csvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
        if (department == null)
        {
            // Department does not exist or is inactive - cannot create user
            result.RecordsFailed++;
            result.Errors.Add($"User {csvData.Email}: Department '{csvData.DepartmentName}' does not exist or is inactive. Please ensure the department exists and is active.");
            return null;
        }

        var assignedManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvData.AssignedToPersonalId);
        var csvFunction = await ResolveExistingFunctionAsync(csvData.Function, functionCache);

        return new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.BasicUser, // Everyone starts as Basic User
            FirstName = csvData.FirstName.Trim(),
            LastName = csvData.LastName.Trim(),
            Email = csvData.Email.Trim(),
            DepartmentId = department.Id,
            AssignedToId = assignedManager?.Id,
            PersonalId = csvData.PersonalId,
            FunctionId = csvFunction?.Id,
            Function = csvFunction,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static void ProcessDeletedItem(UserSyncItemDTO item, Dictionary<string, User> dbUserMap, List<User> usersToDelete, SyncResultDTO result)
    {
        // Soft delete user if hasn't been updated in 90 days
        if (!dbUserMap.TryGetValue(item.Id, out var userToDelete))
        {
            return;
        }

        if (userToDelete.UpdatedAt != null && userToDelete.UpdatedAt > DateTime.UtcNow.AddDays(-90))
        {
            result.RecordsSkipped++;
            return; // Skip deletion
        }

        userToDelete.DeletedAt = DateTime.UtcNow;
        usersToDelete.Add(userToDelete);
        result.RecordsProcessed++;
    }

    private async Task RecordRejectedConflictsAsync(List<FieldConflictDTO> conflicts, User existingUser, ImportHistory importHistory)
    {
        foreach (var conflict in conflicts)
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

    private async Task<bool> ApplySelectedConflictResolutionsAsync(List<FieldConflictDTO> conflicts, CsvUserDTO csvData, User existingUser, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, ImportHistory importHistory, SyncResultDTO result)
    {
        bool hasChanges = false;

        foreach (var conflict in conflicts.Where(c => c.Selected))
        {
            // If no SelectedValue is specified, default to "csv"
            var selectedValue = conflict.SelectedValue ?? "csv";

            if (selectedValue != "csv")
            {
                continue;
            }

            switch (conflict.Field.ToLower())
            {
                case "firstname":
                    hasChanges |= await ApplyFirstNameResolutionAsync(csvData, existingUser, importHistory);
                    break;
                case "lastname":
                    hasChanges |= await ApplyLastNameResolutionAsync(csvData, existingUser, importHistory);
                    break;
                case "email":
                    hasChanges |= await ApplyEmailResolutionAsync(csvData, existingUser, importHistory);
                    break;
                case "departmentname":
                    hasChanges |= await ApplyDepartmentResolutionAsync(csvData, existingUser, departments, importHistory, result);
                    break;
                case "assignedtoname":
                    hasChanges |= await ApplyManagerResolutionAsync(csvData, existingUser, dbUsers, importHistory);
                    break;
                case "function":
                    hasChanges |= await ApplyFunctionResolutionAsync(csvData, existingUser, functionCache, importHistory);
                    break;
            }
        }

        return hasChanges;
    }

    private async Task<bool> ApplyFirstNameResolutionAsync(CsvUserDTO csvData, User existingUser, ImportHistory importHistory)
    {
        if (existingUser.FirstName == csvData.FirstName)
        {
            return false;
        }

        var importConflict = new UserChangeHistory
        {
            Id = Guid.NewGuid(),
            ImportHistoryId = importHistory.Id,
            UserId = existingUser.Id,
            FieldName = "firstname",
            OldValue = existingUser.FirstName,
            NewValue = csvData.FirstName.Trim(),
            Status = "accepted"
        };

        existingUser.FirstName = csvData.FirstName.Trim();
        await _userChangeHistoryRepository.AddAsync(importConflict);
        return true;
    }

    private async Task<bool> ApplyLastNameResolutionAsync(CsvUserDTO csvData, User existingUser, ImportHistory importHistory)
    {
        if (existingUser.LastName == csvData.LastName)
        {
            return false;
        }

        var importConflict = new UserChangeHistory
        {
            Id = Guid.NewGuid(),
            ImportHistoryId = importHistory.Id,
            UserId = existingUser.Id,
            FieldName = "lastname",
            OldValue = existingUser.LastName,
            NewValue = csvData.LastName.Trim(),
            Status = "accepted"
        };

        existingUser.LastName = csvData.LastName.Trim();
        await _userChangeHistoryRepository.AddAsync(importConflict);
        return true;
    }

    private async Task<bool> ApplyEmailResolutionAsync(CsvUserDTO csvData, User existingUser, ImportHistory importHistory)
    {
        if (existingUser.Email == csvData.Email)
        {
            return false;
        }

        var importConflict = new UserChangeHistory
        {
            Id = Guid.NewGuid(),
            ImportHistoryId = importHistory.Id,
            UserId = existingUser.Id,
            FieldName = "email",
            OldValue = existingUser.Email,
            NewValue = csvData.Email,
            Status = "accepted"
        };

        existingUser.Email = csvData.Email;
        await _userChangeHistoryRepository.AddAsync(importConflict);
        return true;
    }

    private async Task<bool> ApplyDepartmentResolutionAsync(CsvUserDTO csvData, User existingUser, List<Department> departments, ImportHistory importHistory, SyncResultDTO result)
    {
        var department = departments.FirstOrDefault(d => d.Name.Equals(csvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
        if (department == null)
        {
            // Department does not exist or is inactive - skip this field update
            result.Errors.Add($"User {csvData.Email}: Cannot update department to '{csvData.DepartmentName}' - department does not exist or is inactive.");
            return false;
        }

        if (existingUser.DepartmentId == department.Id)
        {
            return false;
        }

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
        await _userChangeHistoryRepository.AddAsync(userChangeHistory);
        return true;
    }

    private async Task<bool> ApplyManagerResolutionAsync(CsvUserDTO csvData, User existingUser, List<User> dbUsers, ImportHistory importHistory)
    {
        var newAssignedTo = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvData.AssignedToPersonalId);
        var newAssignedToId = newAssignedTo?.Id;

        if (existingUser.AssignedToId == newAssignedToId)
        {
            return false;
        }

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
        await _userChangeHistoryRepository.AddAsync(userChangeHistory);
        return true;
    }

    private async Task<bool> ApplyFunctionResolutionAsync(CsvUserDTO csvData, User existingUser, Dictionary<string, Function?> functionCache, ImportHistory importHistory)
    {
        var selectedCsvFunction = await ResolveExistingFunctionAsync(csvData.Function, functionCache);
        var selectedCsvFunctionName = csvData.Function?.Trim();

        if (existingUser.FunctionId == selectedCsvFunction?.Id)
        {
            return false;
        }

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
        await _userChangeHistoryRepository.AddAsync(userChangeHistory);
        return true;
    }

    private async Task<(bool Success, bool HasChanges)> ApplyAllDifferingFieldsAsync(CsvUserDTO csvData, User existingUser, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, SyncResultDTO result)
    {
        bool hasChanges = false;

        if (existingUser.FirstName != csvData.FirstName)
        {
            existingUser.FirstName = csvData.FirstName.Trim();
            hasChanges = true;
        }
        if (existingUser.LastName != csvData.LastName)
        {
            existingUser.LastName = csvData.LastName.Trim();
            hasChanges = true;
        }

        var csvFunction = await ResolveExistingFunctionAsync(csvData.Function, functionCache);
        if (existingUser.FunctionId != csvFunction?.Id)
        {
            existingUser.FunctionId = csvFunction?.Id;
            existingUser.Function = csvFunction;
            hasChanges = true;
        }

        var department = departments.FirstOrDefault(d => d.Name.Equals(csvData.DepartmentName, StringComparison.OrdinalIgnoreCase));
        if (department == null)
        {
            // Department does not exist or is inactive - skip this user update
            result.RecordsFailed++;
            result.Errors.Add($"User {csvData.Email}: Department '{csvData.DepartmentName}' does not exist or is inactive. Cannot update user.");
            return (false, hasChanges);
        }
        if (existingUser.DepartmentId != department.Id)
        {
            existingUser.DepartmentId = department.Id;
            hasChanges = true;
        }

        var assignedToManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvData.AssignedToPersonalId);
        var assignedToId = assignedToManager?.Id;

        if (existingUser.AssignedToId != assignedToId)
        {
            existingUser.AssignedToId = assignedToId;
            hasChanges = true;
        }

        return (true, hasChanges);
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
