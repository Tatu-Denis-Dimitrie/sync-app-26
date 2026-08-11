using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
using SyncApp26.Shared.DTOs.Response.PeriodicTraining;

namespace SyncApp26.Infrastructure.Services
{
    public class PeriodicTrainingService : IPeriodicTrainingService
    {
        private readonly ApplicationDbContext _context;

        public PeriodicTrainingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PeriodicTrainingResponseDTO> CreateAsync(CreatePeriodicTrainingDTO dto)
        {
            var instructor = await ResolveInstructorAsync(dto.InstructorId, dto.UserId);

            var training = new PeriodicTraining
            {
                UserId = dto.UserId,
                TrainingDate = dto.TrainingDate,
                DurationHours = dto.DurationHours,
                Occupation = dto.Occupation,
                MaterialTaught = dto.MaterialTaught,
                InstructorId = instructor.Id,
                InstructorName = $"{instructor.FirstName} {instructor.LastName}",
                VerifierName = dto.VerifierName,
                CreatedAt = DateTime.UtcNow
            };

            _context.PeriodicTrainings.Add(training);
            await _context.SaveChangesAsync();

            return MapToDTO(training);
        }

        // Looks up and validates the instructor account for a training row: must exist and must
        // not be the same person as the trainee.
        private async Task<User> ResolveInstructorAsync(Guid instructorId, Guid traineeUserId)
        {
            if (instructorId == traineeUserId)
                throw new ArgumentException("An employee cannot be their own instructor.");

            var instructor = await _context.Users.FindAsync(instructorId);
            if (instructor == null)
                throw new ArgumentException("Instructor not found.");

            return instructor;
        }

        public async Task<PeriodicTrainingResponseDTO?> GetByIdAsync(Guid id)
        {
            var training = await _context.PeriodicTrainings
                .FirstOrDefaultAsync(pt => pt.Id == id);

            return training == null ? null : MapToDTO(training);
        }

        public async Task<IEnumerable<PeriodicTrainingResponseDTO>> GetByUserIdAsync(Guid userId)
        {
            var trainings = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == userId)
                .OrderBy(pt => pt.TrainingDate)
                .ToListAsync();

            return trainings.Select(MapToDTO);
        }

        public async Task<PeriodicTrainingResponseDTO> UpdateAsync(Guid id, UpdatePeriodicTrainingDTO dto)
        {
            var training = await _context.PeriodicTrainings.FindAsync(id);
            if (training == null)
                throw new ArgumentException("Periodic training not found");

            var instructor = await ResolveInstructorAsync(dto.InstructorId, training.UserId);

            bool contentChanged = training.MaterialTaught != dto.MaterialTaught
                || training.DurationHours != dto.DurationHours
                || training.TrainingDate != dto.TrainingDate
                || training.InstructorId != instructor.Id;

            if (contentChanged && IsSigned(training))
                await InvalidateSignatureForRevisionAsync(training);

            training.TrainingDate = dto.TrainingDate;
            training.DurationHours = dto.DurationHours;
            training.Occupation = dto.Occupation;
            training.MaterialTaught = dto.MaterialTaught;
            training.InstructorId = instructor.Id;
            training.InstructorName = $"{instructor.FirstName} {instructor.LastName}";
            training.VerifierName = dto.VerifierName;
            training.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return MapToDTO(training);
        }

        // Excluding (or restoring) a row must cover its whole family: CopyHistoricalPeriodicTrainingRowsAsync
        // duplicates signed rows into every regenerated document, linked by SourceRowId. Acting on just
        // one member would let the row reappear in already-generated documents that hold a sibling copy.
        public async Task<PeriodicTrainingResponseDTO> SetPrintExclusionAsync(Guid id, bool excluded, Guid actingAdminId)
        {
            var training = await _context.PeriodicTrainings.FindAsync(id);
            if (training == null)
                throw new ArgumentException("Periodic training not found");

            var rootId = training.SourceRowId ?? training.Id;
            var family = await _context.PeriodicTrainings
                .Where(pt => pt.Id == rootId || pt.SourceRowId == rootId)
                .ToListAsync();

            foreach (var row in family)
            {
                row.ExcludedFromPrintAt = excluded ? DateTime.UtcNow : null;
                row.ExcludedFromPrintById = excluded ? actingAdminId : null;
            }

            await _context.SaveChangesAsync();

            return MapToDTO(training);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var training = await _context.PeriodicTrainings.FindAsync(id);
            if (training == null)
                return false;

            if (IsSigned(training))
                await InvalidateSignatureForRevisionAsync(training);

            _context.PeriodicTrainings.Remove(training);
            await _context.SaveChangesAsync();

            return true;
        }

        private static bool IsSigned(PeriodicTraining training) =>
            !string.IsNullOrEmpty(training.UserSignatureData)
            || !string.IsNullOrEmpty(training.InstructorSignature)
            || !string.IsNullOrEmpty(training.VerifierSignature);

        // Revising a signed training's content must force a fresh signature — the row's own
        // signature fields are cleared, and if this is the linked document's current training
        // row, the document itself is reset back into the pending-signature queue. The
        // SignatureRecord audit rows already written are never touched: past signatures stay
        // immutable history, this only changes what's currently authoritative going forward.
        private async Task InvalidateSignatureForRevisionAsync(PeriodicTraining training)
        {
            training.UserSignatureData = null;
            training.UserSignatureMethod = null;
            training.InstructorSignature = null;
            training.InstructorSignatureMethod = null;
            training.VerifierSignature = null;
            training.VerifierSignatureMethod = null;

            if (!training.UserDocumentId.HasValue)
                return;

            var currentRow = (await _context.PeriodicTrainings
                    .Where(pt => pt.UserDocumentId == training.UserDocumentId.Value)
                    .ToListAsync())
                .OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();
            if (currentRow?.Id != training.Id)
                return;

            var document = await _context.UserDocuments.FindAsync(training.UserDocumentId.Value);
            if (document == null)
                return;

            document.Status = "PendingUser";
            document.UserSignatureMethod = null;
            document.UserSignatureData = null;
            document.UserSignatureIpAddress = null;
            document.UserSignedAt = null;
            document.UserCryptographicSignature = null;
            document.ManagerSignatureMethod = null;
            document.ManagerSignatureData = null;
            document.ManagerSignatureIpAddress = null;
            document.ManagerSignedAt = null;
            document.ManagerCryptographicSignature = null;
            document.AdminSignatureMethod = null;
            document.AdminSignatureData = null;
            document.AdminSignatureIpAddress = null;
            document.AdminSignedAt = null;
            document.AdminCryptographicSignature = null;
        }

        public async Task<BulkCreateResultDTO> BulkCreateAsync(BulkCreatePeriodicTrainingDTO dto, Guid? restrictToAssignedToId = null)
        {
            var result = new BulkCreateResultDTO();

            try
            {
                if (restrictToAssignedToId.HasValue)
                {
                    var myEmployeeIds = await _context.Users
                        .Where(u => u.AssignedToId == restrictToAssignedToId.Value)
                        .Select(u => u.Id)
                        .ToListAsync();

                    if (dto.ApplyToAllUsers || dto.SelectedUserIds == null || !dto.SelectedUserIds.Any())
                    {
                        dto.ApplyToAllUsers = false;
                        dto.SelectedUserIds = myEmployeeIds;
                    }
                    else
                    {
                        dto.SelectedUserIds = dto.SelectedUserIds.Intersect(myEmployeeIds).ToList();
                    }
                }

                // Get list of users to apply training to
                var usersQuery = _context.Users
                    .Include(u => u.Function)
                    .AsQueryable();

                if (dto.SelectedDepartmentId.HasValue)
                {
                    usersQuery = usersQuery.Where(u => u.DepartmentId == dto.SelectedDepartmentId.Value);
                }

                if (!dto.ApplyToAllUsers)
                {
                    usersQuery = usersQuery.Where(u => dto.SelectedUserIds.Contains(u.Id));
                }

                var users = await usersQuery.ToListAsync();

                if (!users.Any())
                {
                    result.Errors.Add("No users found to apply training to");
                    return result;
                }

                // Determine which document types to create rows for
                var docTypes = dto.DocumentType == "Both"
                    ? new[] { "SSM", "SU" }
                    : new[] { dto.DocumentType ?? "SSM" };

                // Create training record for each user × each document type. InstructorId/InstructorName
                // are left null here — they're stamped at signing time (see
                // DocumentService.ApplySignatureToPeriodicTraining) with whoever actually signs as the
                // SSM/SU officer, not chosen upfront.
                foreach (var user in users)
                {
                    try
                    {
                        foreach (var docType in docTypes)
                        {
                            // Check if there's already an unsigned, unlinked row for this user+type
                            // with the SAME training date — reuse it to prevent exact duplicates.
                            var existingUnsigned = await _context.PeriodicTrainings
                                .Where(pt => pt.UserId == user.Id
                                    && pt.DocumentType == docType
                                    && pt.UserDocumentId == null
                                    && pt.TrainingDate == dto.TrainingDate
                                    && string.IsNullOrEmpty(pt.UserSignatureData)
                                    && string.IsNullOrEmpty(pt.InstructorSignature))
                                .OrderByDescending(pt => pt.CreatedAt)
                                .FirstOrDefaultAsync();

                            if (existingUnsigned != null)
                            {
                                existingUnsigned.TrainingDate = dto.TrainingDate ?? DateTime.UtcNow;
                                existingUnsigned.DurationHours = dto.DurationHours;
                                existingUnsigned.Occupation = !string.IsNullOrWhiteSpace(dto.Occupation)
                                    ? dto.Occupation
                                    : user.Function?.Name;
                                existingUnsigned.MaterialTaught = dto.MaterialTaught;
                                existingUnsigned.VerifierName = dto.VerifierName;
                                existingUnsigned.DocumentType = docType;
                                existingUnsigned.UpdatedAt = DateTime.UtcNow;
                            }
                            else
                            {
                                var training = new PeriodicTraining
                                {
                                    UserId = user.Id,
                                    DocumentType = docType,
                                    TrainingDate = dto.TrainingDate ?? DateTime.UtcNow,
                                    DurationHours = dto.DurationHours,
                                    Occupation = !string.IsNullOrWhiteSpace(dto.Occupation)
                                        ? dto.Occupation
                                        : user.Function?.Name,
                                    MaterialTaught = dto.MaterialTaught,
                                    VerifierName = dto.VerifierName,
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.PeriodicTrainings.Add(training);
                            }
                        }

                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Errors.Add($"Failed for user {user.Email}: {ex.Message}");
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Bulk operation failed: {ex.Message}");
            }

            return result;
        }

        private static PeriodicTrainingResponseDTO MapToDTO(PeriodicTraining training)
        {
            return new PeriodicTrainingResponseDTO
            {
                Id = training.Id,
                UserId = training.UserId,
                DocumentType = training.DocumentType,
                SourceRowId = training.SourceRowId,
                TrainingDate = training.TrainingDate,
                DurationHours = training.DurationHours,
                Occupation = training.Occupation,
                MaterialTaught = training.MaterialTaught,
                InstructorId = training.InstructorId,
                InstructorName = training.InstructorName,
                // map signature fields
                UserSignatureData = training.UserSignatureData,
                UserSignatureMethod = training.UserSignatureMethod,
                InstructorSignature = training.InstructorSignature,
                InstructorSignatureMethod = training.InstructorSignatureMethod,
                VerifierSignature = training.VerifierSignature,
                VerifierSignatureMethod = training.VerifierSignatureMethod,
                VerifierName = training.VerifierName,
                CreatedAt = training.CreatedAt,
                UpdatedAt = training.UpdatedAt,
                ExcludedFromPrintAt = training.ExcludedFromPrintAt
            };
        }
    }
}
