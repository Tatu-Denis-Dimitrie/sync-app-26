using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Application.Services
{
    public class ImpersonationService : IImpersonationService
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly IImpersonationLogRepository _logRepository;

        public ImpersonationService(IUserService userService, ITokenService tokenService, IImpersonationLogRepository logRepository)
        {
            _userService = userService;
            _tokenService = tokenService;
            _logRepository = logRepository;
        }

        // Deliberately absent here, on purpose — do not add either without a fresh discussion:
        //  - IsEmailVerified check: CSV-synced/admin-created accounts leave this null, and an admin
        //    must still be able to view exactly those accounts.
        //  - Re-authentication / password challenge for the admin: out of scope for this feature.
        public async Task<ImpersonationResult> StartAsync(Guid impersonatorUserId, Guid targetUserId, string? ipAddress)
        {
            if (impersonatorUserId == targetUserId)
            {
                return new ImpersonationResult { Status = ImpersonationStatus.SelfImpersonation };
            }

            // GetUserByIdAsync filters DeletedAt == null, so a soft-deleted target is covered for free.
            var target = await _userService.GetUserByIdAsync(targetUserId);
            if (target == null)
            {
                return new ImpersonationResult { Status = ImpersonationStatus.TargetNotFound };
            }

            // RoleAssignments is already eagerly loaded by GetUserByIdAsync — no second round-trip
            // via IsInRoleAsync needed.
            var roleNames = target.RoleAssignments.Select(a => a.Role.Name).ToList();
            if (roleNames.Contains(Roles.Admin))
            {
                return new ImpersonationResult { Status = ImpersonationStatus.TargetIsAdmin };
            }

            // Log first, mint second: if the audit write fails, no token is ever produced.
            await _logRepository.AddAsync(new ImpersonationLog
            {
                ImpersonatorUserId = impersonatorUserId,
                TargetUserId = targetUserId,
                IpAddress = ipAddress
            });

            var token = await _tokenService.GenerateImpersonationTokenAsync(target.Id, target.Email, roleNames, impersonatorUserId);

            return new ImpersonationResult
            {
                Status = ImpersonationStatus.Success,
                Token = token,
                UserId = target.Id,
                Email = target.Email,
                FirstName = target.FirstName,
                LastName = target.LastName,
                Roles = roleNames
            };
        }
    }
}
