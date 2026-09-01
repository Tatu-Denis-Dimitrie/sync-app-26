using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.DataChange;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SyncApp26.Application.Services
{
    public class DataChangeRequestService : IDataChangeRequestService
    {
        // "Department", "Function" and "WorkSite" all travel as the related entity's *name*, not its
        // id - the client picks a name from a dropdown/free-text field (see availableFields on the
        // basic-user/line-manager components), and that's what reads naturally everywhere the value is
        // shown (this admin screen, UserChangeHistory, a CSV import). On the User entity, though, each
        // one is a navigation property, not a string, so the reflection-based applier below can't
        // write it directly - all three need the same explicit name -> entity resolution as WorkSite
        // originally did on its own.
        private const string DepartmentField = nameof(User.Department);
        private const string FunctionField = nameof(User.Function);
        private const string WorkSiteField = nameof(User.WorkSite);

        private static readonly HashSet<string> NavigationNameFields = new(StringComparer.OrdinalIgnoreCase)
        {
            DepartmentField, FunctionField, WorkSiteField
        };

        // Every field the self-service UI actually offers (see availableFields on the
        // basic-user/line-manager components). An allowlist, not a denylist: a new User property -
        // PasswordHash, tokens, DeletedAt, an FK column, anything - is safe by default and must be
        // added here explicitly before this flow can ever touch it. Email is deliberately absent;
        // it has its own domain-checked path below and must never be settable through this one.
        private static readonly HashSet<string> AllowedFieldNames = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(User.FirstName),
            nameof(User.LastName),
            nameof(User.DateOfBirth),
            nameof(User.PlaceOfBirth),
            DepartmentField,
            FunctionField,
            WorkSiteField,
            nameof(User.Address),
            nameof(User.BadgeNumber),
            nameof(User.BloodType),
            nameof(User.CommuteRoute),
            nameof(User.CommuteDurationMinutes)
        };

        // AllowedFieldNames plus Email: RequestEmailChangeAsync builds its own {"Email": ...}
        // payload through a separate, pre-validated path and reuses this same create/resolve
        // pipeline, so Email has to be writable here even though a client can never request it
        // directly (CreateRequestAsync strips anything outside this set before saving anything).
        private static readonly HashSet<string> WritableFieldNames =
            new(AllowedFieldNames, StringComparer.OrdinalIgnoreCase) { nameof(User.Email) };

        public IReadOnlyCollection<string> AllowedFields => AllowedFieldNames;

        private readonly IDataChangeRequestRepository _repository;
        private readonly IUserChangeHistoryRepository _userChangeHistoryRepository;
        private readonly IUserService _userService;
        private readonly IDocumentSignatureService _documentSignatureService;
        private readonly IWorkSiteRepository _workSiteRepository;
        private readonly IDepartmentRepository _departmentRepository;
        private readonly IFunctionRepository _functionRepository;
        private readonly ILogger<DataChangeRequestService> _logger;
        private readonly IStringLocalizer _localizer;

        public DataChangeRequestService(
            IDataChangeRequestRepository repository,
            IUserChangeHistoryRepository userChangeHistoryRepository,
            IUserService userService,
            IDocumentSignatureService documentSignatureService,
            IWorkSiteRepository workSiteRepository,
            IDepartmentRepository departmentRepository,
            IFunctionRepository functionRepository,
            ILogger<DataChangeRequestService> logger,
            ILocalizationService localizationService)
        {
            _repository = repository;
            _userChangeHistoryRepository = userChangeHistoryRepository;
            _userService = userService;
            _documentSignatureService = documentSignatureService;
            _workSiteRepository = workSiteRepository;
            _departmentRepository = departmentRepository;
            _functionRepository = functionRepository;
            _logger = logger;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Requests);
        }

        private static string GetNavigationFieldCurrentName(string fieldKey, User user)
        {
            if (string.Equals(fieldKey, DepartmentField, StringComparison.OrdinalIgnoreCase))
            {
                return user.Department?.Name ?? string.Empty;
            }
            if (string.Equals(fieldKey, FunctionField, StringComparison.OrdinalIgnoreCase))
            {
                return user.Function?.Name ?? string.Empty;
            }
            return user.WorkSite?.Name ?? string.Empty;
        }

        private async Task<(Guid? Id, string CanonicalName, object? Entity)> ResolveNavigationTargetAsync(string fieldKey, string requestedName)
        {
            if (string.Equals(fieldKey, DepartmentField, StringComparison.OrdinalIgnoreCase))
            {
                var department = await _departmentRepository.GetByNameAsync(requestedName);
                if (department == null)
                {
                    throw new Exception(_localizer["resolve.departmentNoLongerExists", requestedName]);
                }
                if (!department.IsActive)
                {
                    throw new Exception(_localizer["resolve.departmentNotActive", department.Name]);
                }
                return (department.Id, department.Name, department);
            }

            if (string.Equals(fieldKey, FunctionField, StringComparison.OrdinalIgnoreCase))
            {
                var function = await _functionRepository.GetByNameAsync(requestedName);
                if (function == null)
                {
                    throw new Exception(_localizer["resolve.functionNoLongerExists", requestedName]);
                }
                return (function.Id, function.Name, function);
            }

            var workSite = await _workSiteRepository.GetByNameAsync(requestedName);
            if (workSite == null)
            {
                throw new Exception(_localizer["resolve.workSiteNoLongerExists", requestedName]);
            }
            if (!workSite.IsActive)
            {
                throw new Exception(_localizer["resolve.workSiteNotActive", workSite.Name]);
            }
            return (workSite.Id, workSite.Name, workSite);
        }

        private static void ApplyNavigationFieldTarget(string fieldKey, User user, Guid? id, object? entity)
        {
            if (string.Equals(fieldKey, DepartmentField, StringComparison.OrdinalIgnoreCase))
            {
                user.DepartmentId = id;
                user.Department = entity as Department;
            }
            else if (string.Equals(fieldKey, FunctionField, StringComparison.OrdinalIgnoreCase))
            {
                user.FunctionId = id;
                user.Function = entity as Function;
            }
            else
            {
                user.WorkSiteId = id;
                user.WorkSite = entity as WorkSite;
            }
        }

        private static Type? GetEnumType(Type propertyType)
        {
            if (propertyType.IsEnum) return propertyType;
            var underlying = Nullable.GetUnderlyingType(propertyType);
            return underlying != null && underlying.IsEnum ? underlying : null;
        }

        private DataChangeRequestDTO MapToDTO(DataChangeRequest req)
        {
            return new DataChangeRequestDTO
            {
                Id = req.Id,
                UserId = req.UserId,
                UserEmail = req.User?.Email ?? string.Empty,
                UserFullName = req.User != null ? $"{req.User.FirstName} {req.User.LastName}" : string.Empty,
                RequestedChangesJson = req.RequestedChangesJson,
                OriginalValuesJson = req.OriginalValuesJson,
                Reason = req.Reason,
                Status = req.Status,
                CreatedAt = req.CreatedAt,
                ResolvedAt = req.ResolvedAt,
                ResolvedByAdminId = req.ResolvedByAdminId,
                AutoResolvedByImportHistoryId = req.AutoResolvedByImportHistoryId
            };
        }

        public async Task<IEnumerable<DataChangeRequestDTO>> GetAllRequestsAsync()
        {
            var requests = await _repository.GetAllWithUserAsync();
            return requests.Select(MapToDTO);
        }

        public async Task<int> GetPendingCountAsync()
        {
            var pending = await _repository.GetAllPendingAsync();
            return pending.Count();
        }

        public async Task<IEnumerable<DataChangeRequestDTO>> GetRequestsByUserAsync(Guid userId)
        {
            var requests = await _repository.GetByUserWithUserAsync(userId);
            return requests.Select(MapToDTO);
        }

        public async Task<DataChangeRequestDTO?> GetRequestByIdAsync(Guid id)
        {
            var req = await _repository.GetByIdWithUserAsync(id);
            return req == null ? null : MapToDTO(req);
        }

        public Task<DataChangeRequestDTO> CreateRequestAsync(Guid userId, CreateDataChangeRequestDTO dto, string initialStatus = "Pending") =>
            CreateRequestCoreAsync(userId, dto, initialStatus, AllowedFieldNames);

        // Not on the interface on purpose - only RequestEmailChangeAsync may pass WritableFieldNames.
        private async Task<DataChangeRequestDTO> CreateRequestCoreAsync(Guid userId, CreateDataChangeRequestDTO dto, string initialStatus, HashSet<string> fieldSet)
        {
            var user = await _repository.GetUserByIdAsync(userId);

            // The real security boundary - the controller's own check is just a friendlier early copy.
            var filteredChangesJson = FilterToFieldSet(dto.RequestedChangesJson, fieldSet);

            var req = new DataChangeRequest
            {
                UserId = userId,
                RequestedChangesJson = filteredChangesJson,
                OriginalValuesJson = BuildOriginalValuesJson(user, filteredChangesJson),
                Reason = dto.Reason,
                Status = initialStatus
            };

            await _repository.AddAsync(req);
            req.User = user!; // userId is always the authenticated caller's own id, so this always resolves
            return MapToDTO(req);
        }

        private static string FilterToFieldSet(string? requestedChangesJson, HashSet<string> fieldSet)
        {
            if (string.IsNullOrWhiteSpace(requestedChangesJson)) return requestedChangesJson ?? string.Empty;

            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(requestedChangesJson);
                if (changes == null) return "{}";

                var filtered = changes.Where(kv => fieldSet.Contains(kv.Key))
                                       .ToDictionary(kv => kv.Key, kv => kv.Value);
                return JsonSerializer.Serialize(filtered);
            }
            catch
            {
                // Can't filter what won't parse - drop it rather than persist an unfiltered payload.
                return "{}";
            }
        }

        // Email can't go through CreateRequestAsync/Create like other fields (see BlockedFields
        // below) because it needs its own validation: the new address must stay on the caller's
        // current domain (this is for renaming a company mailbox after e.g. marriage, not switching
        // to an unrelated address - see the plan doc for why no inbox verification is used here).
        // The request still lands as a normal "Pending" row for an admin to approve, same as any
        // other field change.
        public async Task<AccountActionResult<DataChangeRequestDTO>> RequestEmailChangeAsync(Guid userId, RequestEmailChangeDTO dto)
        {
            var normalizedNewEmail = dto.NewEmail?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!Regex.IsMatch(normalizedNewEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.invalidFormat"]);
            }

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.userNotFound"]);
            }

            var currentDomain = user.Email.Split('@').Last();
            var newDomain = normalizedNewEmail.Split('@').Last();
            if (!string.Equals(currentDomain, newDomain, StringComparison.OrdinalIgnoreCase))
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.sameDomainRequired", currentDomain]);
            }

            if (string.Equals(normalizedNewEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.alreadyCurrent"]);
            }

            var existingUser = await _userService.GetUserByEmailAsync(normalizedNewEmail);
            if (existingUser != null)
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.alreadyInUse"]);
            }

            var existingRequests = await _repository.GetByUserWithUserAsync(userId);
            var hasPendingEmailChange = existingRequests.Any(r => r.Status == "Pending" && TryGetRequestedEmail(r.RequestedChangesJson) != null);
            if (hasPendingEmailChange)
            {
                return AccountActionResult<DataChangeRequestDTO>.Fail(_localizer["emailChange.alreadyPending"]);
            }

            var changesJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Email"] = normalizedNewEmail });
            var created = await CreateRequestCoreAsync(userId, new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = changesJson,
                Reason = string.IsNullOrWhiteSpace(dto.Reason) ? _localizer["emailChange.defaultReason"].Value : dto.Reason!
            }, "Pending", WritableFieldNames);

            return AccountActionResult<DataChangeRequestDTO>.Ok(created);
        }

        private static string? TryGetRequestedValue(string requestedChangesJson, string key)
        {
            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(requestedChangesJson);
                if (changes != null)
                {
                    foreach (var kv in changes)
                    {
                        if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            return kv.Value?.ToString()?.Trim() ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
                // Malformed JSON is surfaced (and reported) wherever the request is actually resolved.
            }
            return null;
        }

        private static string? TryGetRequestedEmail(string requestedChangesJson)
        {
            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(requestedChangesJson);
                if (changes != null && changes.TryGetValue("Email", out var value))
                {
                    return value?.ToString();
                }
            }
            catch
            {
                // Malformed JSON is surfaced (and reported) wherever the request is actually resolved.
            }
            return null;
        }

        // Dates render as the same "yyyy-MM-dd" the request itself carries, so comparing a stored
        // original against a requested value is a like-for-like string diff. Left to ToString(),
        // a DateTime would pick up server culture and never match, logging a change on every resolve.
        private static string? FormatFieldValue(object? value) => value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => value?.ToString()
        };

        // Snapshots the User's current value for every field named in the requested changes, so
        // that resolving the request later can always diff against "what it was when requested"
        // instead of "whatever the live value happens to be right now".
        private static string? BuildOriginalValuesJson(User? user, string requestedChangesJson)
        {
            if (user == null) return null;

            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(requestedChangesJson);
                if (changes == null) return null;

                var userType = typeof(User);
                var originalValues = new Dictionary<string, string?>();
                foreach (var key in changes.Keys)
                {
                    if (NavigationNameFields.Contains(key))
                    {
                        originalValues[key] = GetNavigationFieldCurrentName(key, user);
                        continue;
                    }

                    var prop = userType.GetProperty(key);
                    if (prop != null)
                    {
                        originalValues[key] = FormatFieldValue(prop.GetValue(user));
                    }
                }

                return JsonSerializer.Serialize(originalValues);
            }
            catch
            {
                // Malformed RequestedChangesJson is handled (and reported) at resolve time; don't
                // fail request creation over it.
                return null;
            }
        }

        public async Task<DataChangeRequestDTO> ChangeStatusAsync(Guid id, string status)
        {
            var req = await _repository.GetByIdWithUserAsync(id);
            if (req == null) throw new Exception(_localizer["messages.requestNotFound"]);

            req.Status = status;
            await _repository.UpdateAsync(req);
            return MapToDTO(req);
        }

        public async Task<DataChangeRequestDTO> ResolveRequestAsync(Guid id, Guid adminId, ResolveDataChangeRequestDTO dto)
        {
            var req = await _repository.GetByIdWithUserAsync(id);

            if (req == null) throw new Exception(_localizer["messages.requestNotFound"]);
            if (req.Status != "Pending") throw new Exception(_localizer["messages.alreadyResolved"]);

            // Re-check email uniqueness right before applying: the address could've been claimed by
            // someone else in the time between the request being made and an admin approving it.
            string? oldEmailForCleanup = null;
            if (dto.Status == "Approved")
            {
                var requestedEmail = TryGetRequestedEmail(req.RequestedChangesJson);
                if (requestedEmail != null)
                {
                    var conflictUser = await _userService.GetUserByEmailAsync(requestedEmail);
                    if (conflictUser != null && conflictUser.Id != req.UserId)
                    {
                        throw new Exception(_localizer["resolve.emailTakenSince", requestedEmail]);
                    }
                    oldEmailForCleanup = req.User.Email;
                }
            }

            var resolvedNavigationTargets = new Dictionary<string, (Guid? Id, string CanonicalName, object? Entity)>(StringComparer.OrdinalIgnoreCase);
            if (dto.Status == "Approved")
            {
                foreach (var field in NavigationNameFields)
                {
                    var requestedName = TryGetRequestedValue(req.RequestedChangesJson, field);
                    if (requestedName == null)
                    {
                        continue; // request doesn't touch this field
                    }

                    resolvedNavigationTargets[field] = requestedName.Length > 0
                        ? await ResolveNavigationTargetAsync(field, requestedName)
                        : (null, string.Empty, null);
                }
            }

            req.Status = dto.Status;
            req.ResolvedAt = DateTime.UtcNow;
            req.ResolvedByAdminId = adminId;

            var historyEntries = new List<UserChangeHistory>();
            var now = DateTime.UtcNow;
            var statusLower = dto.Status.ToLower(); // "approved" or "rejected"

            Dictionary<string, string?>? originalValues = null;
            if (!string.IsNullOrWhiteSpace(req.OriginalValuesJson))
            {
                try
                {
                    originalValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(req.OriginalValuesJson);
                }
                catch
                {
                    originalValues = null;
                }
            }

            try
            {
                var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(req.RequestedChangesJson);
                if (changes != null)
                {
                    var userType = typeof(User);

                    // Capture old values and build history entries
                    foreach (var kv in changes)
                    {
                        // Belt-and-suspenders: CreateRequestAsync already strips anything outside
                        // WritableFieldNames before persisting, but a request stored before that
                        // filter existed could still carry a stale, now-disallowed key - keep it out
                        // of history too, not just out of the apply step below.
                        if (!WritableFieldNames.Contains(kv.Key)) continue;

                        if (NavigationNameFields.Contains(kv.Key))
                        {
                            var oldName = (originalValues != null && originalValues.TryGetValue(kv.Key, out var navigationSnapshot))
                                ? navigationSnapshot ?? string.Empty
                                : GetNavigationFieldCurrentName(kv.Key, req.User);
                            var newName = kv.Value?.ToString() ?? string.Empty;

                            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                            {
                                historyEntries.Add(new UserChangeHistory
                                {
                                    Id = Guid.NewGuid(),
                                    UserId = req.UserId,
                                    FieldName = kv.Key,
                                    OldValue = oldName,
                                    NewValue = newName,
                                    ImportHistoryId = null,
                                    Status = statusLower,
                                    CreatedAt = now
                                });
                            }

                            continue;
                        }

                        var prop = userType.GetProperty(kv.Key);
                        if (prop != null)
                        {
                            var oldValue = (originalValues != null && originalValues.TryGetValue(kv.Key, out var snapshotValue))
                                ? snapshotValue ?? string.Empty
                                : FormatFieldValue(prop.GetValue(req.User)) ?? string.Empty;
                            var newValue = kv.Value?.ToString() ?? string.Empty;

                            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                            {
                                historyEntries.Add(new UserChangeHistory
                                {
                                    Id = Guid.NewGuid(),
                                    UserId = req.UserId,
                                    FieldName = kv.Key,
                                    OldValue = oldValue,
                                    NewValue = newValue,
                                    ImportHistoryId = null,
                                    Status = statusLower,
                                    CreatedAt = now
                                });
                            }
                        }
                    }

                    // Apply changes to user only if approved
                    if (dto.Status == "Approved")
                    {
                        foreach (var kv in changes)
                        {
                            if (!WritableFieldNames.Contains(kv.Key)) continue; // Same stale-row guard as above
                            if (NavigationNameFields.Contains(kv.Key))
                            {
                                var target = resolvedNavigationTargets.TryGetValue(kv.Key, out var resolved)
                                    ? resolved
                                    : (Id: (Guid?)null, CanonicalName: string.Empty, Entity: (object?)null);
                                ApplyNavigationFieldTarget(kv.Key, req.User, target.Id, target.Entity);
                                continue;
                            }

                            var prop = userType.GetProperty(kv.Key);
                            if (prop != null && prop.CanWrite)
                            {
                                var stringValue = kv.Value?.ToString();

                                if (prop.PropertyType == typeof(string))
                                    prop.SetValue(req.User, stringValue);
                                else if (prop.PropertyType == typeof(Guid) && Guid.TryParse(stringValue, out var g))
                                    prop.SetValue(req.User, g);
                                else if (prop.PropertyType == typeof(Guid?) && Guid.TryParse(stringValue, out var ng))
                                    prop.SetValue(req.User, ng);
                                else if (prop.PropertyType == typeof(int) && int.TryParse(stringValue, out var it))
                                    prop.SetValue(req.User, it);
                                else if (prop.PropertyType == typeof(int?) && int.TryParse(stringValue, out var nit))
                                    prop.SetValue(req.User, nit);
                                // Date fields (e.g. DateOfBirth) arrive as the browser's "yyyy-MM-dd";
                                // parsed invariantly so the result doesn't shift with server culture,
                                // and as a plain calendar date rather than an instant to convert.
                                else if ((prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                                         && DateTime.TryParse(stringValue, CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out var dt))
                                    prop.SetValue(req.User, dt);
                                else if (GetEnumType(prop.PropertyType) is { } enumType
                                         && !string.IsNullOrWhiteSpace(stringValue)
                                         && Enum.TryParse(enumType, stringValue, ignoreCase: true, out var enumValue)
                                         && Enum.IsDefined(enumType, enumValue))
                                    prop.SetValue(req.User, enumValue);
                            }
                        }
                        req.User.UpdatedAt = DateTime.UtcNow;
                        await _repository.UpdateUserAsync(req.User);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying resolved changes for data change request {RequestId}.", req.Id);
                throw new Exception(_localizer["resolve.errorProcessing"]);
            }

            await _repository.UpdateAsync(req);

            // Save history entries
            foreach (var entry in historyEntries)
            {
                await _userChangeHistoryRepository.AddAsync(entry);
            }

            if (oldEmailForCleanup != null)
            {
                try
                {
                    await _documentSignatureService.InvalidateTokensForEmailAsync(oldEmailForCleanup);
                }
                catch
                {
                    // Best-effort - a stale signing link failing later with "Signer account not
                    // found" is an acceptable fallback; it must never block the approval itself.
                }
            }

            return MapToDTO(req);
        }
    }
}
