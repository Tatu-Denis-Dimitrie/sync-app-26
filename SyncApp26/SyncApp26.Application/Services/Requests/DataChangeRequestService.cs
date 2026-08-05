using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.DataChange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SyncApp26.Application.Services
{
    public class DataChangeRequestService : IDataChangeRequestService
    {
        // Fields that must never be applied through this generic reflection-based flow,
        // even if a client crafts a request bypassing the UI's available-fields list.
        private static readonly HashSet<string> BlockedFields = new(StringComparer.OrdinalIgnoreCase) { "Email", "Role" };

        private readonly IDataChangeRequestRepository _repository;
        private readonly IUserChangeHistoryRepository _userChangeHistoryRepository;

        public DataChangeRequestService(IDataChangeRequestRepository repository, IUserChangeHistoryRepository userChangeHistoryRepository)
        {
            _repository = repository;
            _userChangeHistoryRepository = userChangeHistoryRepository;
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
                UserEmail = req.User?.Email,
                UserFullName = req.User != null ? $"{req.User.FirstName} {req.User.LastName}" : null,
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

        public async Task<DataChangeRequestDTO> GetRequestByIdAsync(Guid id)
        {
            var req = await _repository.GetByIdWithUserAsync(id);
            return req == null ? null : MapToDTO(req);
        }

        public async Task<DataChangeRequestDTO> CreateRequestAsync(Guid userId, CreateDataChangeRequestDTO dto, string initialStatus = "Pending")
        {
            var user = await _repository.GetUserByIdAsync(userId);

            var req = new DataChangeRequest
            {
                UserId = userId,
                RequestedChangesJson = dto.RequestedChangesJson,
                OriginalValuesJson = BuildOriginalValuesJson(user, dto.RequestedChangesJson),
                Reason = dto.Reason,
                Status = initialStatus
            };

            await _repository.AddAsync(req);
            req.User = user;
            return MapToDTO(req);
        }

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
                    var prop = userType.GetProperty(key);
                    if (prop != null)
                    {
                        originalValues[key] = prop.GetValue(user)?.ToString();
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
            if (req == null) throw new Exception("Request not found");
            
            req.Status = status;
            await _repository.UpdateAsync(req);
            return MapToDTO(req);
        }

        public async Task<DataChangeRequestDTO> ResolveRequestAsync(Guid id, Guid adminId, ResolveDataChangeRequestDTO dto)
        {
            var req = await _repository.GetByIdWithUserAsync(id);

            if (req == null) throw new Exception("Request not found");
            if (req.Status != "Pending") throw new Exception("Request is already resolved");

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
                        var prop = userType.GetProperty(kv.Key);
                        if (prop != null)
                        {
                            // Prefer the value snapshotted when the request was created. Falling back to
                            // the live value only applies to legacy requests created before this snapshot
                            // existed - otherwise, diffing against the live value would silently produce no
                            // history entry whenever another write path (e.g. a CSV import) already applied
                            // this same change to the user before the request was resolved.
                            var oldValue = (originalValues != null && originalValues.TryGetValue(kv.Key, out var snapshotValue))
                                ? snapshotValue ?? string.Empty
                                : prop.GetValue(req.User)?.ToString() ?? string.Empty;
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
                            if (BlockedFields.Contains(kv.Key)) continue; // Explicitly block Email/Role changes
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
                Console.WriteLine($"Error processing changes: {ex.Message}");
                throw new Exception("Error processing data change request.");
            }

            await _repository.UpdateAsync(req);

            // Save history entries
            foreach (var entry in historyEntries)
            {
                await _userChangeHistoryRepository.AddAsync(entry);
            }

            return MapToDTO(req);
        }
    }
}
