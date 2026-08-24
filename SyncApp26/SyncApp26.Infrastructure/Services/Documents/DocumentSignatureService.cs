using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using System.Security.Cryptography;

namespace SyncApp26.Infrastructure.Services
{
    public class DocumentSignatureService : IDocumentSignatureService
    {
        private readonly ApplicationDbContext _context;

        public DocumentSignatureService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<string> GenerateSignatureTokenAsync(string email, Guid documentId, string documentName, Guid? periodicTrainingId = null)
        {
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            var token = new DocumentSignatureToken
            {
                Email = email,
                DocumentId = documentId,
                DocumentName = documentName,
                Token = tokenString,
                ExpiresAt = DateTime.UtcNow.AddDays(7), // Valid for 7 days
                PeriodicTrainingId = periodicTrainingId
            };

            _context.DocumentSignatureTokens.Add(token);
            await _context.SaveChangesAsync();

            return tokenString;
        }

        public async Task<DocumentSignatureToken?> ValidateTokenAsync(string token)
        {
            var query = await _context.DocumentSignatureTokens
                .FirstOrDefaultAsync(t => t.Token == token);

            if (query == null) return null;
            if (query.IsUsed || query.ExpiresAt < DateTime.UtcNow) return null;

            return query;
        }

        public async Task<bool> ConsumeTokenAsync(string token)
        {
            var dbToken = await _context.DocumentSignatureTokens
                .FirstOrDefaultAsync(t => t.Token == token);

            if (dbToken == null) return false;
            if (dbToken.IsUsed || dbToken.ExpiresAt < DateTime.UtcNow) return false;

            dbToken.IsUsed = true;
            await _context.SaveChangesAsync();

            return true;
        }

        // Signing tokens are keyed by a raw email string, not UserId - an outstanding link for an
        // address that just got changed away from would otherwise fail post-change with "Signer
        // account not found" instead of a clean re-request. Marking used (not deleting) keeps the
        // row around for audit, same as a normal consume.
        public async Task<int> InvalidateTokensForEmailAsync(string email)
        {
            var tokens = await _context.DocumentSignatureTokens
                .Where(t => t.Email == email && !t.IsUsed && t.ExpiresAt >= DateTime.UtcNow)
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.IsUsed = true;
            }

            if (tokens.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return tokens.Count;
        }
    }
}
