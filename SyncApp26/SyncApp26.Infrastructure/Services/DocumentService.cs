using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Cryptography;

namespace SyncApp26.Infrastructure.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICryptographyService _cryptographyService;

        public DocumentService(ApplicationDbContext context, ICryptographyService cryptographyService)
        {
            _context = context;
            _cryptographyService = cryptographyService;
        }

        public async Task<UserDocument> GenerateDocumentAsync(Guid userId, string documentType, string generatedByEmail)
        {
            var user = await _context.Users
                .Include(u => u.AssignedTo)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                throw new ArgumentException("User not found.");
            }

            var doc = new UserDocument
            {
                UserId = userId,
                DocumentType = documentType,
                Status = "PendingUser",
                GeneratedAt = DateTime.UtcNow
            };

            // Generate PDF snapshot
            var pdfPath = await GeneratePdfSnapshotAsync(user, doc);
            doc.PdfFilePath = pdfPath;

            _context.UserDocuments.Add(doc);
            await _context.SaveChangesAsync();

            return doc;
        }

        public async Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document)
        {
            // Set License - you can move this to Program.cs if desired
            QuestPDF.Settings.License = LicenseType.Community;

            var docsFolder = Path.Combine(Directory.GetCurrentDirectory(), "GeneratedDocuments");
            if (!Directory.Exists(docsFolder))
            {
                Directory.CreateDirectory(docsFolder);
            }

            var fileName = $"{document.DocumentType}_{user.FirstName}_{user.LastName}_{document.Id}.pdf";
            var filePath = Path.Combine(docsFolder, fileName);

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text($"{document.DocumentType} Document for {user.FirstName} {user.LastName}")
                        .SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(x =>
                        {
                            x.Spacing(20);

                            x.Item().Text($"Employee: {user.FirstName} {user.LastName}");
                            x.Item().Text($"Email: {user.Email}");
                            x.Item().Text($"Personal ID: {user.PersonalId}");
                            x.Item().Text($"Department: {user.Department?.Name ?? "N/A"}");
                            x.Item().Text($"Function: {user.Function?.Name ?? "N/A"}");
                            x.Item().Text($"Date Generated: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

                            // We can add all the other training data here in a real production scenario
                            x.Item().Text("--- Training Information ---");
                            x.Item().Text($"Intro Training Date: {user.IntroductoryTrainingDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
                            x.Item().Text($"Workplace Training Date: {user.WorkplaceTrainingDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
                            
                            x.Item().Text("This document is a snapshot of the employee's data at the time of generation.");
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                        });
                });
            })
            .GeneratePdf(filePath);

            // Compute SHA-256 Hash of the generated PDF
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    document.DocumentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }

            return filePath;
        }

        public async Task<UserDocument?> GetDocumentByIdAsync(Guid documentId)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        public async Task<IEnumerable<UserDocument>> GetUserDocumentsAsync(Guid userId)
        {
            return await _context.UserDocuments
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress)
        {
            var doc = await _context.UserDocuments.FirstOrDefaultAsync(d => d.Id == documentId);
            if (doc == null) return false;

            var timestamp = DateTime.UtcNow;
            
            // Construct the payload to sign
            var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
            var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

            if (isUserSignature)
            {
                doc.UserSignatureMethod = signatureMethod;
                doc.UserSignatureData = signatureData;
                doc.UserSignatureIpAddress = ipAddress;
                doc.UserSignedAt = timestamp;
                doc.UserCryptographicSignature = cryptoSignature;
                doc.Status = "PendingManager";
            }
            else
            {
                doc.ManagerSignatureMethod = signatureMethod;
                doc.ManagerSignatureData = signatureData;
                doc.ManagerSignatureIpAddress = ipAddress;
                doc.ManagerSignedAt = timestamp;
                doc.ManagerCryptographicSignature = cryptoSignature;
                doc.Status = "Completed";
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
