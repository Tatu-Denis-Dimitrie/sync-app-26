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
            var training = new PeriodicTraining
            {
                UserId = dto.UserId,
                TrainingDate = dto.TrainingDate,
                DurationHours = dto.DurationHours,
                Occupation = dto.Occupation,
                MaterialTaught = dto.MaterialTaught,
                InstructorName = dto.InstructorName,
                VerifierName = dto.VerifierName,
                CreatedAt = DateTime.UtcNow
            };

            _context.PeriodicTrainings.Add(training);
            await _context.SaveChangesAsync();

            return MapToDTO(training);
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

            training.TrainingDate = dto.TrainingDate;
            training.DurationHours = dto.DurationHours;
            training.Occupation = dto.Occupation;
            training.MaterialTaught = dto.MaterialTaught;
            training.InstructorName = dto.InstructorName;
            training.VerifierName = dto.VerifierName;
            training.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return MapToDTO(training);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var training = await _context.PeriodicTrainings.FindAsync(id);
            if (training == null)
                return false;

            _context.PeriodicTrainings.Remove(training);
            await _context.SaveChangesAsync();

            return true;
        }

        private static PeriodicTrainingResponseDTO MapToDTO(PeriodicTraining training)
        {
            return new PeriodicTrainingResponseDTO
            {
                Id = training.Id,
                UserId = training.UserId,
                TrainingDate = training.TrainingDate,
                DurationHours = training.DurationHours,
                Occupation = training.Occupation,
                MaterialTaught = training.MaterialTaught,
                InstructorName = training.InstructorName,
                VerifierName = training.VerifierName,
                CreatedAt = training.CreatedAt,
                UpdatedAt = training.UpdatedAt
            };
        }
    }
}
