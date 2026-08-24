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
using System.Text.Json;

namespace SyncApp26.Application.Services;

public class CsvSyncService : ICsvSyncService
{
    private readonly ISyncNotificationService _notificationService;
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IFunctionRepository _functionRepository;
    private readonly IWorkSiteRepository _workSiteRepository;
    private readonly IImportHistoryRepository _importHistoryRepository;
    private readonly IUserChangeHistoryRepository _userChangeHistoryRepository;
    private readonly IDataChangeRequestRepository _dataChangeRequestRepository;

    // Fields a pending DataChangeRequest can name (as User property names) that this service knows
    // how to compare against a CSV value textually. DataChangeRequest stores raw property values
    // (e.g. a DepartmentId Guid), while the CSV compares department/function/manager by display
    // name, so those aren't included here - there's no reliable text equivalence to auto-match on.
    private static readonly Dictionary<string, string> CsvFieldToUserProperty = new(StringComparer.OrdinalIgnoreCase)
    {
        { "firstname", nameof(User.FirstName) },
        { "lastname", nameof(User.LastName) }
    };

    public CsvSyncService(IUserRepository userRepository, IDepartmentRepository departmentRepository, IFunctionRepository functionRepository, IWorkSiteRepository workSiteRepository, ISyncNotificationService notificationService, IImportHistoryRepository importHistoryRepository, IUserChangeHistoryRepository userChangeHistoryRepositoryRepository, IDataChangeRequestRepository dataChangeRequestRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _functionRepository = functionRepository;
        _workSiteRepository = workSiteRepository;
        _notificationService = notificationService;
        _importHistoryRepository = importHistoryRepository;
        _userChangeHistoryRepository = userChangeHistoryRepositoryRepository;
        _dataChangeRequestRepository = dataChangeRequestRepository;
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

        // Group pending DataChangeRequests by user so conflicts on the same field can be flagged
        var pendingRequestsByUserId = (await _dataChangeRequestRepository.GetAllPendingAsync())
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

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
                pendingRequestsByUserId.TryGetValue(dbUser.Id, out var pendingRequestsForUser);
                var comparison = await BuildExistingUserComparisonAsync(dbUser, csvUser, dbUsers, pendingRequestsForUser ?? new List<DataChangeRequest>());
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

    private async Task<UserComparisonDTO> BuildExistingUserComparisonAsync(User dbUser, CsvUserDTO csvUser, List<User> dbUsers, List<DataChangeRequest> pendingRequestsForUser)
    {
        // User exists - compare fields
        var csvManager = await ResolveLineManagerByPersonalIdAsync(dbUsers, csvUser.AssignedToPersonalId);
        var csvUserData = MapToCsvUserDataDTO(csvUser, csvManager);
        var conflicts = DetectFieldConflicts(dbUser, csvUser, csvManager, pendingRequestsForUser);

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
            Function = csvUser.Function != null ? csvUser.Function.Trim() : null,
            WorkSite = csvUser.WorkSite != null ? csvUser.WorkSite.Trim() : null
        };
    }

    private static List<FieldConflictDTO> DetectFieldConflicts(User dbUser, CsvUserDTO csvUser, User? csvManager, List<DataChangeRequest> pendingRequestsForUser)
    {
        var conflicts = new List<FieldConflictDTO>();
        var pendingValuesByCsvField = GetPendingRequestValuesByCsvField(pendingRequestsForUser);

        // firstName/lastName also surface a pending request that's gone stale relative to the DB -
        // even on a row where the CSV itself doesn't disagree with the DB - so admins don't lose
        // track of a request like "Popescu" sitting pending after "Fernandez" was already applied.
        AddNameFieldConflict(conflicts, "firstName", "firstname", dbUser.FirstName, csvUser.FirstName, pendingValuesByCsvField);
        AddNameFieldConflict(conflicts, "lastName", "lastname", dbUser.LastName, csvUser.LastName, pendingValuesByCsvField);

        var dbDepartmentName = dbUser.Department?.Name;
        AddFieldConflictIfDifferent(conflicts, "departmentName", dbDepartmentName ?? string.Empty, csvUser.DepartmentName, dbDepartmentName != csvUser.DepartmentName);

        AddFieldConflictIfDifferent(conflicts, "email", dbUser.Email, csvUser.Email, dbUser.Email != csvUser.Email);

        var dbFunctionName = dbUser.Function?.Name?.Trim();
        var csvFunctionName = csvUser.Function?.Trim();
        AddFieldConflictIfDifferent(conflicts, "function", dbFunctionName ?? string.Empty, csvFunctionName,
            !string.Equals(dbFunctionName, csvFunctionName, StringComparison.OrdinalIgnoreCase));

        var dbWorkSiteName = dbUser.WorkSite?.Name?.Trim();
        var csvWorkSiteName = csvUser.WorkSite?.Trim();
        AddFieldConflictIfDifferent(conflicts, "workSite", dbWorkSiteName ?? string.Empty, csvWorkSiteName,
            !string.Equals(dbWorkSiteName, csvWorkSiteName, StringComparison.OrdinalIgnoreCase));

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

    private static void AddNameFieldConflict(List<FieldConflictDTO> conflicts, string field, string csvFieldKey, string dbValue, string csvValue, Dictionary<string, List<(Guid RequestId, string Value)>> pendingValuesByCsvField)
    {
        var valuesDiffer = dbValue != csvValue;

        pendingValuesByCsvField.TryGetValue(csvFieldKey, out var pendingTargets);
        var pendingOptions = BuildPendingFieldOptions(dbValue, csvValue, pendingTargets);

        if (!valuesDiffer && pendingOptions.Count == 0)
        {
            return;
        }

        conflicts.Add(new FieldConflictDTO
        {
            Field = field,
            DbValue = dbValue,
            CsvValue = csvValue,
            Selected = false,
            HasPendingRequest = pendingOptions.Count > 0,
            PendingRequestValue = pendingOptions.Count > 0 ? string.Join(", ", pendingOptions.Select(o => o.Value)) : null,
            PendingOptions = pendingOptions.Select(o => new PendingRequestOptionDTO { Value = o.Value }).ToList()
        });
    }

    private sealed record PendingFieldOption(string Value, List<Guid> RequestIds);

    private static List<PendingFieldOption> BuildPendingFieldOptions(string dbValue, string csvValue, List<(Guid RequestId, string Value)>? pendingTargets)
    {
        if (pendingTargets == null || pendingTargets.Count == 0)
        {
            return new List<PendingFieldOption>();
        }

        var options = pendingTargets
            .GroupBy(p => p.Value, StringComparer.Ordinal)
            .Select(g => new PendingFieldOption(g.Key, g.Select(p => p.RequestId).Distinct().ToList()))
            .ToList();

        var nothingAtStake = string.Equals(dbValue, csvValue, StringComparison.Ordinal)
            && options.All(o => string.Equals(o.Value, csvValue, StringComparison.Ordinal));

        return nothingAtStake ? new List<PendingFieldOption>() : options;
    }

    // Maps CSV field keys (e.g. "lastname") to every distinct value a pending DataChangeRequest for
    // this user is asking that field to become, along with the id of the request asking for it.
    private static Dictionary<string, List<(Guid RequestId, string Value)>> GetPendingRequestValuesByCsvField(List<DataChangeRequest> pendingRequestsForUser)
    {
        var result = new Dictionary<string, List<(Guid RequestId, string Value)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in pendingRequestsForUser)
        {
            foreach (var (propertyName, value) in TryGetRequestedFieldValues(request.RequestedChangesJson))
            {
                var csvField = CsvFieldToUserProperty
                    .FirstOrDefault(kv => string.Equals(kv.Value, propertyName, StringComparison.OrdinalIgnoreCase))
                    .Key;
                if (csvField == null)
                {
                    continue;
                }

                if (!result.TryGetValue(csvField, out var values))
                {
                    values = new List<(Guid RequestId, string Value)>();
                    result[csvField] = values;
                }
                values.Add((request.Id, value));
            }
        }
        return result;
    }

    private static List<(string PropertyName, string Value)> TryGetRequestedFieldValues(string requestedChangesJson)
    {
        try
        {
            var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(requestedChangesJson);
            return changes?.Select(kv => (kv.Key, kv.Value?.ToString() ?? string.Empty)).ToList()
                   ?? new List<(string, string)>();
        }
        catch
        {
            return new List<(string, string)>();
        }
    }

    private List<UserComparisonDTO> FindDeletedUserComparisons(List<User> dbUsers, IEnumerable<CsvUserDTO> csvUsers)
    {
        // Find deleted users (in DB but not in CSV) by PersonalId
        var csvPersonalIds = csvUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.PersonalId))
            .Select(u => u.PersonalId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedComparisons = new List<UserComparisonDTO>();
        // Only CSV-managed accounts are in scope: for a seeded or self-registered account, absence
        // from an HR export means nothing, so flagging it as "deleted" every single import is noise
        // that also invites a mis-click into wiping accounts the CSV never owned.
        foreach (var dbUser in dbUsers.Where(u => u.IsCsvManaged))
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
        var workSiteCache = new Dictionary<string, WorkSite?>(StringComparer.OrdinalIgnoreCase);

        // Fetched once, not per new row - an import can create thousands of users in one pass.
        var basicUserRoleId = (await _userRepository.GetRoleByNameAsync(Roles.BasicUser))?.Id;

        // Pending DataChangeRequests grouped by user, so a request satisfied by this import can be
        // auto-closed with correct audit attribution instead of silently going stale.
        var pendingRequestsByUserId = (await _dataChangeRequestRepository.GetAllPendingAsync())
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

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

        async Task EnsureImportHistoryCreatedAsync()
        {
            if (!importHistoryCreated)
            {
                await _importHistoryRepository.AddAsync(importHistory);
                importHistoryCreated = true;
            }
        }

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
                    var newUser = await TryBuildNewUserAsync(item.CsvData, departments, dbUsers, functionCache, workSiteCache, basicUserRoleId, result);
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
                        pendingRequestsByUserId.TryGetValue(existingUser.Id, out var pendingRequestsForUser);
                        pendingRequestsForUser ??= new List<DataChangeRequest>();

                        if (item.Conflicts.Any())
                        {
                            await EnsureImportHistoryCreatedAsync();
                            await RecordRejectedConflictsAsync(item.Conflicts, existingUser, importHistory);
                        }

                        bool hasChanges;

                        // If conflicts exist, apply only selected resolutions; otherwise sync every differing field
                        if (item.Conflicts.Any())
                        {
                            hasChanges = await ApplySelectedConflictResolutionsAsync(item.Conflicts, csvData, existingUser, departments, dbUsers, functionCache, workSiteCache, importHistory, result, pendingRequestsForUser);
                        }
                        else
                        {
                            var (success, changed) = await ApplyAllDifferingFieldsAsync(csvData, existingUser, departments, dbUsers, functionCache, workSiteCache, result);
                            if (!success)
                            {
                                continue;
                            }
                            hasChanges = changed;
                        }

                        await AutoResolveSatisfiedRequestsAsync(existingUser, pendingRequestsForUser, importHistory, EnsureImportHistoryCreatedAsync);

                        // Appearing in the CSV is itself the proof that the CSV owns this person, so
                        // adopt accounts that predate this flag or were first created another way -
                        // otherwise their eventual departure would never be detected. UpdatedAt is
                        // deliberately left alone: this is bookkeeping, not a change to their data.
                        bool adopted = !existingUser.IsCsvManaged;
                        if (adopted)
                        {
                            existingUser.IsCsvManaged = true;
                        }

                        if (hasChanges)
                        {
                            existingUser.UpdatedAt = DateTime.UtcNow;
                            usersToUpdate.Add(existingUser);
                            result.RecordsProcessed++;
                        }
                        else
                        {
                            if (adopted)
                            {
                                usersToUpdate.Add(existingUser);
                            }
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
                    .Where(u => managerIds.Contains(u.Id) && !u.RoleAssignments.Any(a => a.Role.Name == Roles.LineManager))
                    .ToList();
                var usersToDemote = allUsers
                    .Where(u => !managerIds.Contains(u.Id) && u.RoleAssignments.Any(a => a.Role.Name == Roles.LineManager))
                    .ToList();

                if (usersToPromote.Count > 0 || usersToDemote.Count > 0)
                {
                    var lineManagerRole = await _userRepository.GetRoleByNameAsync(Roles.LineManager);
                    if (lineManagerRole != null)
                    {
                        // Grant/revoke ONLY the LineManager role: every other role the person holds
                        // (officer duties, admin) is none of the import's business and must survive
                        // an import untouched.
                        foreach (var u in usersToPromote)
                            u.RoleAssignments.Add(new UserRoleAssignment { UserId = u.Id, RoleId = lineManagerRole.Id });
                        foreach (var u in usersToDemote)
                            u.RoleAssignments.Remove(u.RoleAssignments.First(a => a.Role.Name == Roles.LineManager));

                        var roleUpdates = usersToPromote.Concat(usersToDemote).ToList();
                        await _userRepository.UpdateUsersAsync(roleUpdates);
                    }
                }
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

    private async Task<User?> TryBuildNewUserAsync(CsvUserDTO csvData, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, Dictionary<string, WorkSite?> workSiteCache, Guid? basicUserRoleId, SyncResultDTO result)
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
        var csvWorkSite = await ResolveExistingWorkSiteAsync(csvData.WorkSite, workSiteCache);

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            FirstName = csvData.FirstName.Trim(),
            LastName = csvData.LastName.Trim(),
            Email = csvData.Email.Trim(),
            DepartmentId = department.Id,
            AssignedToId = assignedManager?.Id,
            PersonalId = csvData.PersonalId,
            FunctionId = csvFunction?.Id,
            Function = csvFunction,
            WorkSiteId = csvWorkSite?.Id,
            WorkSite = csvWorkSite,
            IsCsvManaged = true,
            CreatedAt = DateTime.UtcNow
        };

        // Everyone starts as Basic User. A missing role id means the Roles table isn't seeded/migrated
        // yet — creating the account anyway would silently produce a role-less user nothing can
        // authorize, so fail the row explicitly instead of proceeding.
        if (!basicUserRoleId.HasValue)
        {
            result.RecordsFailed++;
            result.Errors.Add($"User {csvData.Email}: The BasicUser role is not configured. Cannot create the account without it.");
            return null;
        }
        newUser.RoleAssignments.Add(new UserRoleAssignment { UserId = newUser.Id, RoleId = basicUserRoleId.Value });

        return newUser;
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
            if (selectedValue.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Purely informational conflicts (e.g. a stale pending DataChangeRequest flagged on a
            // field where the DB and CSV already agree) have nothing to reject - don't log a no-op.
            if (Equals(conflict.DbValue?.ToString(), conflict.CsvValue?.ToString()))
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

    // Closes out any pending DataChangeRequest for this user whose every requested field now
    // already equals the user's live value (i.e. this import - possibly combined with an earlier
    // one - achieved exactly what the request was asking for). Requests naming any field this
    // service can't textually compare against a CSV value, or whose fields don't ALL match yet,
    // are left pending for an admin to resolve normally.
    private async Task AutoResolveSatisfiedRequestsAsync(User existingUser, List<DataChangeRequest> pendingRequestsForUser, ImportHistory importHistory, Func<Task> ensureImportHistoryCreatedAsync)
    {
        foreach (var request in pendingRequestsForUser.Where(r => r.Status == "Pending"))
        {
            Dictionary<string, object>? changes;
            try
            {
                changes = JsonSerializer.Deserialize<Dictionary<string, object>>(request.RequestedChangesJson);
            }
            catch
            {
                continue;
            }
            if (changes == null || changes.Count == 0)
            {
                continue;
            }

            var resolvedFields = new List<(string PropertyName, string NewValue)>();
            var fullySatisfied = true;
            foreach (var kv in changes)
            {
                if (!CsvFieldToUserProperty.ContainsValue(kv.Key))
                {
                    fullySatisfied = false;
                    break;
                }

                var prop = typeof(User).GetProperty(kv.Key);
                var newValue = kv.Value?.ToString() ?? string.Empty;
                var currentValue = prop?.GetValue(existingUser)?.ToString() ?? string.Empty;
                if (prop == null || !string.Equals(currentValue, newValue, StringComparison.Ordinal))
                {
                    fullySatisfied = false;
                    break;
                }

                resolvedFields.Add((kv.Key, newValue));
            }

            if (!fullySatisfied)
            {
                continue;
            }

            await ensureImportHistoryCreatedAsync();

            var originalValues = TryDeserializeOriginalValues(request.OriginalValuesJson);
            foreach (var (propertyName, newValue) in resolvedFields)
            {
                var oldValue = (originalValues != null && originalValues.TryGetValue(propertyName, out var snapshot))
                    ? snapshot ?? string.Empty
                    : newValue; // No creation-time snapshot available (legacy request) - nothing meaningful to show as "before".

                if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    continue;
                }

                await _userChangeHistoryRepository.AddAsync(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    ImportHistoryId = importHistory.Id,
                    UserId = existingUser.Id,
                    FieldName = propertyName.ToLowerInvariant(),
                    OldValue = oldValue,
                    NewValue = newValue,
                    Status = "approved-by-import",
                    CreatedAt = DateTime.UtcNow
                });
            }

            request.Status = "Approved";
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByAdminId = null;
            request.AutoResolvedByImportHistoryId = importHistory.Id;
            await _dataChangeRequestRepository.UpdateAsync(request);
        }
    }

    private static Dictionary<string, string?>? TryDeserializeOriginalValues(string? originalValuesJson)
    {
        if (string.IsNullOrWhiteSpace(originalValuesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(originalValuesJson);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ApplySelectedConflictResolutionsAsync(List<FieldConflictDTO> conflicts, CsvUserDTO csvData, User existingUser, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, Dictionary<string, WorkSite?> workSiteCache, ImportHistory importHistory, SyncResultDTO result, List<DataChangeRequest> pendingRequestsForUser)
    {
        bool hasChanges = false;

        var pendingValuesByCsvField = GetPendingRequestValuesByCsvField(pendingRequestsForUser);
        var overriddenRequestIds = new HashSet<Guid>();

        foreach (var conflict in conflicts.Where(c => c.Selected))
        {
            var fieldKey = conflict.Field.ToLower();
            var pendingOptions = pendingValuesByCsvField.TryGetValue(fieldKey, out var targets)
                ? BuildPendingFieldOptions(GetUserNameValue(fieldKey, existingUser), GetCsvNameValue(fieldKey, csvData), targets)
                : new List<PendingFieldOption>();

            string? chosenPendingValue = null;

            if (pendingOptions.Count > 0)
            {
                if (conflict.SelectedValue != "csv" && conflict.SelectedValue != "pending")
                {
                    continue;
                }

                if (conflict.SelectedValue == "pending")
                {
                    chosenPendingValue = ResolveChosenPendingValue(conflict, pendingOptions);
                    if (chosenPendingValue == null)
                    {
                        continue;
                    }
                }
            }
            else if ((conflict.SelectedValue ?? "csv") != "csv")
            {
                continue;
            }

            switch (fieldKey)
            {
                case "firstname":
                    hasChanges |= await ApplyFirstNameResolutionAsync(csvData, existingUser, importHistory, chosenPendingValue);
                    break;
                case "lastname":
                    hasChanges |= await ApplyLastNameResolutionAsync(csvData, existingUser, importHistory, chosenPendingValue);
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
                case "worksite":
                    hasChanges |= await ApplyWorkSiteResolutionAsync(csvData, existingUser, workSiteCache, importHistory);
                    break;
            }

            if (pendingOptions.Count > 0)
            {
                var appliedValue = (chosenPendingValue ?? GetCsvNameValue(fieldKey, csvData)).Trim();
                foreach (var option in pendingOptions.Where(o => !string.Equals(o.Value.Trim(), appliedValue, StringComparison.Ordinal)))
                {
                    foreach (var requestId in option.RequestIds)
                    {
                        overriddenRequestIds.Add(requestId);
                    }
                }
            }
        }

        await RejectOverriddenPendingRequestsAsync(overriddenRequestIds, pendingRequestsForUser, importHistory);

        return hasChanges;
    }


    private static string? ResolveChosenPendingValue(FieldConflictDTO conflict, List<PendingFieldOption> pendingOptions)
    {
        if (conflict.SelectedPendingValue == null)
        {
            return pendingOptions.Count == 1 ? pendingOptions[0].Value : null;
        }

        return pendingOptions
            .FirstOrDefault(o => string.Equals(o.Value, conflict.SelectedPendingValue, StringComparison.Ordinal))
            ?.Value;
    }

    private static string GetCsvNameValue(string fieldKey, CsvUserDTO csvData) =>
        fieldKey == "firstname" ? csvData.FirstName : csvData.LastName;

    private static string GetUserNameValue(string fieldKey, User user) =>
        fieldKey == "firstname" ? user.FirstName : user.LastName;

    private async Task RejectOverriddenPendingRequestsAsync(HashSet<Guid> overriddenRequestIds, List<DataChangeRequest> pendingRequestsForUser, ImportHistory importHistory)
    {
        foreach (var request in pendingRequestsForUser.Where(r => r.Status == "Pending" && overriddenRequestIds.Contains(r.Id)))
        {
            request.Status = "Rejected";
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByAdminId = null;
            request.AutoResolvedByImportHistoryId = importHistory.Id;
            await _dataChangeRequestRepository.UpdateAsync(request);
        }
    }

    private async Task<bool> ApplyFirstNameResolutionAsync(CsvUserDTO csvData, User existingUser, ImportHistory importHistory, string? chosenPendingValue)
    {
        var newValue = (chosenPendingValue ?? csvData.FirstName).Trim();
        if (existingUser.FirstName == newValue)
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
            NewValue = newValue,
            Status = "accepted"
        };

        existingUser.FirstName = newValue;
        await _userChangeHistoryRepository.AddAsync(importConflict);
        return true;
    }

    private async Task<bool> ApplyLastNameResolutionAsync(CsvUserDTO csvData, User existingUser, ImportHistory importHistory, string? chosenPendingValue)
    {
        var newValue = (chosenPendingValue ?? csvData.LastName).Trim();
        if (existingUser.LastName == newValue)
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
            NewValue = newValue,
            Status = "accepted"
        };

        existingUser.LastName = newValue;
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

    private async Task<bool> ApplyWorkSiteResolutionAsync(CsvUserDTO csvData, User existingUser, Dictionary<string, WorkSite?> workSiteCache, ImportHistory importHistory)
    {
        var selectedCsvWorkSite = await ResolveExistingWorkSiteAsync(csvData.WorkSite, workSiteCache);
        var selectedCsvWorkSiteName = csvData.WorkSite?.Trim();

        if (existingUser.WorkSiteId == selectedCsvWorkSite?.Id)
        {
            return false;
        }

        var userChangeHistory = new UserChangeHistory
        {
            Id = Guid.NewGuid(),
            ImportHistoryId = importHistory.Id,
            UserId = existingUser.Id,
            FieldName = "workSite",
            OldValue = existingUser.WorkSite?.Name ?? string.Empty,
            NewValue = selectedCsvWorkSiteName ?? string.Empty,
            Status = "accepted"
        };

        existingUser.WorkSiteId = selectedCsvWorkSite?.Id;
        existingUser.WorkSite = selectedCsvWorkSite;
        await _userChangeHistoryRepository.AddAsync(userChangeHistory);
        return true;
    }

    private async Task<(bool Success, bool HasChanges)> ApplyAllDifferingFieldsAsync(CsvUserDTO csvData, User existingUser, List<Department> departments, List<User> dbUsers, Dictionary<string, Function?> functionCache, Dictionary<string, WorkSite?> workSiteCache, SyncResultDTO result)
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

        var csvWorkSite = await ResolveExistingWorkSiteAsync(csvData.WorkSite, workSiteCache);
        if (existingUser.WorkSiteId != csvWorkSite?.Id)
        {
            existingUser.WorkSiteId = csvWorkSite?.Id;
            existingUser.WorkSite = csvWorkSite;
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
            WorkSiteId = user.WorkSiteId,
            WorkSite = user.WorkSite?.Name,
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

    private async Task<WorkSite?> ResolveExistingWorkSiteAsync(string? workSiteName, Dictionary<string, WorkSite?> workSiteCache)
    {
        if (string.IsNullOrWhiteSpace(workSiteName))
        {
            return null;
        }

        var normalizedName = workSiteName.Trim();
        if (workSiteCache.TryGetValue(normalizedName, out var cachedWorkSite))
        {
            return cachedWorkSite;
        }

        var existingWorkSite = await _workSiteRepository.GetByNameAsync(normalizedName);
        workSiteCache[normalizedName] = existingWorkSite;
        return existingWorkSite;
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
