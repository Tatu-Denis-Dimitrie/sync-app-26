using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IDataChangeRequestRepository
    {
        Task<IEnumerable<DataChangeRequest>> GetAllWithUserAsync();
        Task<IEnumerable<DataChangeRequest>> GetByUserWithUserAsync(Guid userId);
        Task<DataChangeRequest> GetByIdWithUserAsync(Guid id);
        Task<DataChangeRequest> AddAsync(DataChangeRequest request);
        Task UpdateAsync(DataChangeRequest request);
        Task<User> GetUserByIdAsync(Guid userId);
        Task UpdateUserAsync(User user);
    }
}
