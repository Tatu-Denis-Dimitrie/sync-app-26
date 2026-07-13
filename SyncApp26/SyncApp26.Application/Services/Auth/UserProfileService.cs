using System.ComponentModel.DataAnnotations;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;

namespace SyncApp26.Application.Services
{
    public class UserProfileService : IUserProfileService
    {
        private readonly IUserService _userService;
        private readonly IDepartmentService _departmentService;
        private readonly IFunctionService _functionService;
        private readonly IUserChangeHistoryService _userChangeHistoryService;
        private readonly IUserInitialTrainingService _userInitialTrainingService;

        public UserProfileService(
            IUserService userService,
            IDepartmentService departmentService,
            IFunctionService functionService,
            IUserChangeHistoryService userChangeHistoryService,
            IUserInitialTrainingService userInitialTrainingService)
        {
            _userService = userService;
            _departmentService = departmentService;
            _functionService = functionService;
            _userChangeHistoryService = userChangeHistoryService;
            _userInitialTrainingService = userInitialTrainingService;
        }

        private async Task<Guid?> ResolveFunctionIdAsync(string? requestedFunction)
        {
            if (!string.IsNullOrWhiteSpace(requestedFunction))
            {
                var existingFunction = await _functionService.GetByNameAsync(requestedFunction);
                if (existingFunction != null)
                {
                    return existingFunction.Id;
                }
            }

            var unknownFunction = await _functionService.GetByNameAsync("Unknown");
            return unknownFunction?.Id;
        }

        public async Task<UserResponseDTO> CreateUserAsync(UserRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.FirstName) ||
                string.IsNullOrEmpty(request.LastName) ||
                string.IsNullOrEmpty(request.Email))
            {
                return new UserResponseDTO { Success = false, Message = "FirstName, LastName, and Email are required" };
            }

            if (!new EmailAddressAttribute().IsValid(request.Email))
            {
                return new UserResponseDTO { Success = false, Message = "Invalid email format" };
            }

            var department = await _departmentService.GetDepartmentByIdAsync(request.DepartmentId);
            if (department == null)
            {
                return new UserResponseDTO { Success = false, Message = "Department not found" };
            }

            if (request.AssignedToId != null)
            {
                var assignedTo = await _userService.GetUserByIdAsync(request.AssignedToId.Value);
                if (assignedTo == null)
                {
                    return new UserResponseDTO { Success = false, Message = "Assigned to user not found" };
                }
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = Guid.NewGuid().ToString(),
                Role = UserRole.BasicUser,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                DepartmentId = request.DepartmentId,
                AssignedToId = request.AssignedToId,
                FunctionId = await ResolveFunctionIdAsync(request.Function),
                CreatedAt = DateTime.UtcNow
            };

            await _userService.AddUserAsync(user);

            if (request.AssignedToId.HasValue)
            {
                var managerToPromote = await _userService.GetUserByIdAsync(request.AssignedToId.Value);
                if (managerToPromote != null && managerToPromote.Role != UserRole.LineManager)
                {
                    managerToPromote.Role = UserRole.LineManager;
                    await _userService.UpdateUserAsync(managerToPromote);
                }
            }

            return new UserResponseDTO { Success = true, Message = "User created successfully" };
        }

        public async Task<UserResponseDTO> UpdateUserAsync(User existingUser, UserRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.FirstName) ||
                string.IsNullOrEmpty(request.LastName) ||
                string.IsNullOrEmpty(request.Email))
            {
                return new UserResponseDTO { Success = false, Message = "FirstName, LastName, and Email are required" };
            }

            if (!new EmailAddressAttribute().IsValid(request.Email))
            {
                return new UserResponseDTO { Success = false, Message = "Invalid email format" };
            }

            if (request.AssignedToId != null && request.AssignedToId == existingUser.Id)
            {
                return new UserResponseDTO { Success = false, Message = "User cannot be assigned to themselves" };
            }

            var department = await _departmentService.GetDepartmentByIdAsync(request.DepartmentId);
            if (department == null)
            {
                return new UserResponseDTO { Success = false, Message = "Department not found" };
            }

            var oldFirstName = existingUser.FirstName;
            var oldLastName = existingUser.LastName;
            var oldEmail = existingUser.Email;
            var oldDepartmentId = existingUser.DepartmentId;
            var oldDepartmentName = existingUser.Department?.Name ?? string.Empty;
            var oldAssignedToId = existingUser.AssignedToId;
            var oldAssignedToName = existingUser.AssignedTo != null
                ? $"{existingUser.AssignedTo.FirstName} {existingUser.AssignedTo.LastName}".Trim()
                : string.Empty;
            var oldFunctionId = existingUser.FunctionId;
            var oldFunctionName = existingUser.Function?.Name ?? "Unknown";

            var newDepartmentName = department.Name;
            User? assignedToUser = null;

            if (request.AssignedToId != null)
            {
                var assignedTo = await _userService.GetUserByIdAsync(request.AssignedToId.Value);
                if (assignedTo == null)
                {
                    return new UserResponseDTO { Success = false, Message = "Assigned to user not found" };
                }

                assignedToUser = assignedTo;

                if (assignedTo.AssignedToId == existingUser.Id)
                {
                    return new UserResponseDTO { Success = false, Message = "Circular assignment detected: Cannot assign a user to someone who reports to them" };
                }
            }

            var resolvedFunctionId = await ResolveFunctionIdAsync(request.Function);
            var newFunctionName = string.IsNullOrWhiteSpace(request.Function)
                ? "Unknown"
                : request.Function.Trim();
            var newAssignedToName = assignedToUser != null
                ? $"{assignedToUser.FirstName} {assignedToUser.LastName}".Trim()
                : string.Empty;

            existingUser.FirstName = request.FirstName;
            existingUser.LastName = request.LastName;
            existingUser.Email = request.Email;
            existingUser.DepartmentId = request.DepartmentId;
            existingUser.AssignedToId = request.AssignedToId;
            existingUser.FunctionId = resolvedFunctionId;
            existingUser.UpdatedAt = DateTime.UtcNow;

            if (request.Role.HasValue)
            {
                existingUser.Role = request.Role.Value;
            }

            await _userService.UpdateUserAsync(existingUser);

            var now = DateTime.UtcNow;
            var manualChanges = new List<UserChangeHistory>();

            if (!string.Equals(oldFirstName, existingUser.FirstName, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "FirstName",
                    OldValue = oldFirstName,
                    NewValue = existingUser.FirstName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (!string.Equals(oldLastName, existingUser.LastName, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "LastName",
                    OldValue = oldLastName,
                    NewValue = existingUser.LastName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (!string.Equals(oldEmail, existingUser.Email, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "Email",
                    OldValue = oldEmail,
                    NewValue = existingUser.Email,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldDepartmentId != existingUser.DepartmentId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "DepartmentName",
                    OldValue = oldDepartmentName,
                    NewValue = newDepartmentName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldAssignedToId != existingUser.AssignedToId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "AssignedToName",
                    OldValue = oldAssignedToName,
                    NewValue = newAssignedToName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldFunctionId != existingUser.FunctionId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "FunctionName",
                    OldValue = oldFunctionName,
                    NewValue = newFunctionName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            foreach (var change in manualChanges)
            {
                await _userChangeHistoryService.AddUserChangeHistoryAsync(change);
            }

            return new UserResponseDTO { Success = true, Message = "User updated successfully" };
        }

        public async Task UpdateSsmSuFormAsync(User user, UpdateUserSSMSUFormDTO dto)
        {
            user.DateOfBirth = dto.DateOfBirth;
            user.PlaceOfBirth = dto.PlaceOfBirth;
            user.Address = dto.Address;
            user.BloodGroup = dto.BloodGroup;
            user.BadgeNumber = dto.BadgeNumber;
            user.Education = dto.Education;
            user.Qualifications = dto.Qualifications;
            user.CommuteRoute = dto.CommuteRoute;
            user.CommuteDurationMinutes = dto.CommuteDurationMinutes;

            user.AdmittedByName = dto.AdmittedByName;
            user.AdmittedByFunction = dto.AdmittedByFunction;
            user.AdmittedDate = dto.AdmittedDate;

            user.UpdatedAt = DateTime.UtcNow;

            foreach (var entry in dto.InitialTrainings)
            {
                if (string.IsNullOrWhiteSpace(entry.DocumentType)) continue;
                var docType = entry.DocumentType.ToUpper();
                var existing = await _userInitialTrainingService.GetByUserIdAndTypeAsync(user.Id, docType);

                var isNew = existing == null;
                existing ??= new UserInitialTraining { UserId = user.Id, DocumentType = docType, CreatedAt = DateTime.UtcNow };

                existing.IntroductoryTrainingDate = entry.IntroductoryTrainingDate;
                existing.IntroductoryTrainingHours = entry.IntroductoryTrainingHours;
                existing.IntroductoryTrainingInstructor = entry.IntroductoryTrainingInstructor;
                existing.IntroductoryTrainingInstructorFunction = entry.IntroductoryTrainingInstructorFunction;
                existing.IntroductoryTrainingContent = entry.IntroductoryTrainingContent;
                existing.WorkplaceTrainingDate = entry.WorkplaceTrainingDate;
                existing.WorkplaceTrainingLocation = entry.WorkplaceTrainingLocation;
                existing.WorkplaceTrainingHours = entry.WorkplaceTrainingHours;
                existing.WorkplaceTrainingInstructor = entry.WorkplaceTrainingInstructor;
                existing.WorkplaceTrainingInstructorFunction = entry.WorkplaceTrainingInstructorFunction;
                existing.WorkplaceTrainingContent = entry.WorkplaceTrainingContent;
                existing.UpdatedAt = DateTime.UtcNow;

                if (isNew)
                {
                    await _userInitialTrainingService.AddAsync(existing);
                }
                else
                {
                    await _userInitialTrainingService.UpdateAsync(existing);
                }
            }

            await _userService.UpdateUserAsync(user);
        }

        public async Task<BulkInitialTrainingResultDTO> ApplyBulkInitialTrainingAsync(BulkInitialTrainingDTO dto, Guid? restrictToAssignedToId)
        {
            var result = new BulkInitialTrainingResultDTO();

            var users = await _userService.GetAllUsersAsync();
            var targetUsers = users.AsQueryable();

            if (dto.SelectedDepartmentId.HasValue)
                targetUsers = targetUsers.Where(u => u.DepartmentId == dto.SelectedDepartmentId.Value);

            if (restrictToAssignedToId.HasValue)
                targetUsers = targetUsers.Where(u => u.AssignedToId == restrictToAssignedToId.Value);

            if (!dto.ApplyToAllUsers)
            {
                var selected = dto.SelectedUserIds?.ToHashSet() ?? new HashSet<Guid>();
                targetUsers = targetUsers.Where(u => selected.Contains(u.Id));
            }

            var usersToUpdate = targetUsers.ToList();
            if (!usersToUpdate.Any())
            {
                result.NoUsersMatched = true;
                result.Errors.Add("No users found to apply initial training data.");
                return result;
            }

            var docTypes = dto.DocumentType.Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? new[] { "SSM", "SU" }
                : new[] { dto.DocumentType.ToUpper() };

            foreach (var user in usersToUpdate)
            {
                try
                {
                    var changed = false;
                    foreach (var docType in docTypes)
                    {
                        var existing = await _userInitialTrainingService.GetByUserIdAndTypeAsync(user.Id, docType);

                        var isNew = existing == null;
                        existing ??= new UserInitialTraining { UserId = user.Id, DocumentType = docType, CreatedAt = DateTime.UtcNow };
                        if (isNew)
                        {
                            changed = true;
                        }

                        if (!existing.IntroductoryTrainingDate.HasValue && dto.IntroductoryTrainingDate.HasValue)
                        { existing.IntroductoryTrainingDate = dto.IntroductoryTrainingDate; changed = true; }

                        if (!existing.IntroductoryTrainingHours.HasValue && dto.IntroductoryTrainingHours.HasValue)
                        { existing.IntroductoryTrainingHours = dto.IntroductoryTrainingHours; changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.IntroductoryTrainingInstructor) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingInstructor))
                        { existing.IntroductoryTrainingInstructor = dto.IntroductoryTrainingInstructor.Trim(); changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.IntroductoryTrainingInstructorFunction) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingInstructorFunction))
                        { existing.IntroductoryTrainingInstructorFunction = dto.IntroductoryTrainingInstructorFunction.Trim(); changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.IntroductoryTrainingContent) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingContent))
                        { existing.IntroductoryTrainingContent = dto.IntroductoryTrainingContent.Trim(); changed = true; }

                        if (!existing.WorkplaceTrainingDate.HasValue && dto.WorkplaceTrainingDate.HasValue)
                        { existing.WorkplaceTrainingDate = dto.WorkplaceTrainingDate; changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.WorkplaceTrainingLocation) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingLocation))
                        { existing.WorkplaceTrainingLocation = dto.WorkplaceTrainingLocation.Trim(); changed = true; }

                        if (!existing.WorkplaceTrainingHours.HasValue && dto.WorkplaceTrainingHours.HasValue)
                        { existing.WorkplaceTrainingHours = dto.WorkplaceTrainingHours; changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.WorkplaceTrainingInstructor) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingInstructor))
                        { existing.WorkplaceTrainingInstructor = dto.WorkplaceTrainingInstructor.Trim(); changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.WorkplaceTrainingInstructorFunction) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingInstructorFunction))
                        { existing.WorkplaceTrainingInstructorFunction = dto.WorkplaceTrainingInstructorFunction.Trim(); changed = true; }

                        if (string.IsNullOrWhiteSpace(existing.WorkplaceTrainingContent) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingContent))
                        { existing.WorkplaceTrainingContent = dto.WorkplaceTrainingContent.Trim(); changed = true; }

                        if (changed)
                        {
                            existing.UpdatedAt = DateTime.UtcNow;
                            if (isNew)
                            {
                                await _userInitialTrainingService.AddAsync(existing);
                            }
                            else
                            {
                                await _userInitialTrainingService.UpdateAsync(existing);
                            }
                        }
                    }

                    if (!changed) { result.SkippedCount++; continue; }

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"Failed for user {user.Email}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
