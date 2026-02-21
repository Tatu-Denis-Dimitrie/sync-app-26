using SyncApp26.Shared.DTOs;

namespace SyncApp26.Application.IServices
{
    public interface ICsvValidationService
    {
        Task<CsvValidationResultDTO> ValidateCsvFile(Stream fileStream, string fileName);
        bool IsValidEmail(string email);
    }
}