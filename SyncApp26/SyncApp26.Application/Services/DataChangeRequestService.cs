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
        private readonly IDataChangeRequestRepository _repository;

        public DataChangeRequestService(IDataChangeRequestRepository repository)
        {
            _repository = repository;
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
                Reason = req.Reason,
                Status = req.Status,
                CreatedAt = req.CreatedAt,
                ResolvedAt = req.ResolvedAt,
                ResolvedByAdminId = req.ResolvedByAdminId
            };
        }

        public async Task<IEnumerable<DataChangeRequestDTO>> GetAllRequestsAsync()
        {
            var requests = await _repository.GetAllWithUserAsync();
            return requests.Select(MapToDTO);
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
            var req = new DataChangeRequest
            {
                UserId = userId,
                RequestedChangesJson = dto.RequestedChangesJson,
                Reason = dto.Reason,
                Status = initialStatus
            };

            await _repository.AddAsync(req);
            req.User = await _repository.GetUserByIdAsync(userId);
            return MapToDTO(req);
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

            if (dto.Status == "Approved")
            {
                try
                {
                    var changes = JsonSerializer.Deserialize<Dictionary<string, object>>(req.RequestedChangesJson);
                    if (changes != null)
                    {
                        var userType = typeof(User);
                        foreach (var kv in changes)
                        {
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
                            }
                        }
                    }
                    req.User.UpdatedAt = DateTime.UtcNow;
                    await _repository.UpdateUserAsync(req.User);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying changes: {ex.Message}");
                    throw new Exception("Error applying JSON changes to user.");
                }
            }

            await _repository.UpdateAsync(req);
            return MapToDTO(req);
        }
    }
}
