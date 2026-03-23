using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class DataChangeRequestRepository : IDataChangeRequestRepository
    {
        private readonly ApplicationDbContext _context;

        public DataChangeRequestRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<DataChangeRequest>> GetAllWithUserAsync()
        {
            return await _context.DataChangeRequests
                .Include(x => x.User)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<DataChangeRequest>> GetByUserWithUserAsync(Guid userId)
        {
            return await _context.DataChangeRequests
                .Include(x => x.User)
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<DataChangeRequest> GetByIdWithUserAsync(Guid id)
        {
            return await _context.DataChangeRequests
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<DataChangeRequest> AddAsync(DataChangeRequest request)
        {
            _context.DataChangeRequests.Add(request);
            await _context.SaveChangesAsync();
            return request;
        }

        public async Task UpdateAsync(DataChangeRequest request)
        {
            _context.DataChangeRequests.Update(request);
            await _context.SaveChangesAsync();
        }

        public async Task<User> GetUserByIdAsync(Guid userId)
        {
            return await _context.Users.FindAsync(userId);
        }

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }
    }
}
