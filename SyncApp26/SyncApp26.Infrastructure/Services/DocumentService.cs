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

        // Returnează numărul de documente SSM ce trebuie semnate de admin (verificator)
        public async Task<int> GetPendingSsmDocumentsForAdminAsync()
        {
            var count = await _context.UserDocuments
                .Include(d => d.User)
                .Where(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.User != null &&
                    (d.User.Role == null || d.User.Role.Name.ToUpper() != "ADMIN") &&
                    (d.Status == "PendingManager" || d.Status == "PendingUser") &&
                    d.ManagerSignedAt == null)
                .CountAsync();
            return count;
        }

        // Returnează lista de documente SSM ce trebuie semnate de admin (pentru bulk progres)
        public async Task<List<UserDocument>> GetPendingSsmDocumentsForAdminListAsync()
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Where(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.User != null &&
                    (d.User.Role == null || d.User.Role.Name.ToUpper() != "ADMIN") &&
                    (d.Status == "PendingManager" || d.Status == "PendingUser") &&
                    d.ManagerSignedAt == null)
                .ToListAsync();
        }

        public async Task<UserDocument> GenerateDocumentAsync(Guid userId, string documentType, string generatedByEmail)
        {
            Console.WriteLine($"[GENERATE] Starting document generation for UserId: {userId}, DocumentType: {documentType}, GeneratedBy: {generatedByEmail}");
            bool isSsmDocumentType = string.Equals(documentType, "SSM", StringComparison.OrdinalIgnoreCase);

            // Check if user is admin (admins should not have documents generated)
            var userToGenerate = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
            if (userToGenerate?.Role != null && string.Equals(userToGenerate.Role.Name, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot generate documents for admin users.");
            }

            // Before anything else, if there's an existing completed document with signatures,
            // permanently copy those signatures to the correct PeriodicTraining row
            var existingDoc = await _context.UserDocuments
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DocumentType == documentType);

            if (existingDoc != null
                && !string.IsNullOrEmpty(existingDoc.UserSignatureData)
                && !string.IsNullOrEmpty(existingDoc.ManagerSignatureData))
            {
                // Find the PeriodicTraining row that the document was ACTUALLY signed against:
                // it's the newest row that already has at least one signature saved to it
                var rowToFinalize = await _context.PeriodicTrainings
                    .Where(pt => pt.UserId == userId
                        && (!string.IsNullOrEmpty(pt.UserSignatureData)
                            || !string.IsNullOrEmpty(pt.InstructorSignature)
                            || (!string.IsNullOrEmpty(pt.VerifierSignature) && isSsmDocumentType))
                        && (string.IsNullOrEmpty(pt.UserSignatureData)
                            || (isSsmDocumentType
                                ? (string.IsNullOrEmpty(pt.InstructorSignature) && string.IsNullOrEmpty(pt.VerifierSignature))
                                : string.IsNullOrEmpty(pt.InstructorSignature))))
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();

                if (rowToFinalize != null)
                {
                    if (string.IsNullOrEmpty(rowToFinalize.UserSignatureData))
                    {
                        rowToFinalize.UserSignatureData = existingDoc.UserSignatureData;
                        rowToFinalize.UserSignatureMethod = existingDoc.UserSignatureMethod;
                    }
                    await _context.SaveChangesAsync();
                }
            }

            // SU must always create a new table row at generation.
            // SSM may reuse an entirely empty row.
            PeriodicTraining? existingUnsignedTraining = null;
            if (isSsmDocumentType)
            {
                existingUnsignedTraining = await _context.PeriodicTrainings
                    .Where(pt => pt.UserId == userId
                        && string.IsNullOrEmpty(pt.UserSignatureData)
                        && string.IsNullOrEmpty(pt.InstructorSignature)
                        && string.IsNullOrEmpty(pt.VerifierSignature))
                    .OrderByDescending(pt => pt.DurationHours.HasValue ? 1 : 0)
                    .ThenBy(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (existingUnsignedTraining == null)
            {
                // All rows are fully signed, create a new one
                var mostRecentTraining = await _context.PeriodicTrainings
                    .Where(pt => pt.UserId == userId)
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();

                var newTraining = new PeriodicTraining
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TrainingDate = mostRecentTraining?.TrainingDate ?? DateTime.UtcNow,
                    DurationHours = mostRecentTraining?.DurationHours,
                    Occupation = mostRecentTraining?.Occupation,
                    MaterialTaught = mostRecentTraining?.MaterialTaught,
                    InstructorName = mostRecentTraining?.InstructorName,
                    VerifierName = mostRecentTraining?.VerifierName,
                    CreatedAt = DateTime.UtcNow,
                    UserDocumentId = existingDoc?.Id
                };
                _context.PeriodicTrainings.Add(newTraining);
            }
            else
            {
                // Clear any stale signatures on the reused row so regeneration starts fresh
                existingUnsignedTraining.UserSignatureData = null;
                existingUnsignedTraining.UserSignatureMethod = null;
                existingUnsignedTraining.InstructorSignature = null;
                existingUnsignedTraining.InstructorSignatureMethod = null;
                existingUnsignedTraining.VerifierSignature = null;
            }

            // Look for an existing document for this user+type; if found, reuse it
            var doc = existingDoc;

            if (doc != null)
            {
                // Reset for re-signing on the new row
                doc.Status = "PendingUser";
                doc.GeneratedAt = DateTime.UtcNow;
                doc.UserSignatureMethod = null;
                doc.UserSignatureData = null;
                doc.UserSignatureIpAddress = null;
                doc.UserSignedAt = null;
                doc.UserCryptographicSignature = null;
                doc.ManagerSignatureMethod = null;
                doc.ManagerSignatureData = null;
                doc.ManagerSignatureIpAddress = null;
                doc.ManagerSignedAt = null;
                doc.ManagerCryptographicSignature = null;
            }
            else
            {
                doc = new UserDocument
                {
                    UserId = userId,
                    DocumentType = documentType ?? string.Empty,
                    Status = "PendingUser",
                    GeneratedAt = DateTime.UtcNow
                };
                _context.UserDocuments.Add(doc);
            }

            await _context.SaveChangesAsync();

            // If new training was created without UserDocumentId, set it now
            if (existingUnsignedTraining == null && !string.IsNullOrEmpty(doc.Id.ToString()))
            {
                var newlyCreatedTraining = await _context.PeriodicTrainings
                    .Where(pt => pt.UserId == userId && pt.UserDocumentId == null)
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();
                if (newlyCreatedTraining != null)
                {
                    newlyCreatedTraining.UserDocumentId = doc.Id;
                }
            }
            else if (existingUnsignedTraining != null)
            {
                existingUnsignedTraining.UserDocumentId = doc.Id;
            }

            await _context.SaveChangesAsync();

            // ── For SSM: place admin's saved signature as verifier on the latest training row ──
            if (isSsmDocumentType)
            {
                var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == generatedByEmail);
                if (adminUser != null)
                {
                    var adminSig = await _context.UserSignatures
                        .Where(s => s.UserId == adminUser.Id && s.RevokedAt == null)
                        .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (adminSig != null)
                    {
                        var trainingRow = await _context.PeriodicTrainings
                            .Where(pt => pt.UserDocumentId == doc.Id)
                            .OrderByDescending(pt => pt.CreatedAt)
                            .FirstOrDefaultAsync();

                        if (trainingRow != null)
                        {
                            trainingRow.VerifierSignature = adminSig.SignatureData;
                            trainingRow.VerifierSignatureMethod = adminSig.SignatureMethod;
                            await _context.SaveChangesAsync();
                        }
                    }
                }
            }

            // Reload user with all PeriodicTrainings (deterministic order)
            var user = await _context.Users
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new ArgumentException("User not found.");

            var pdfPath = await GeneratePdfSnapshotAsync(user, doc);
            doc.PdfFilePath = pdfPath;
            await _context.SaveChangesAsync();

            return doc;
        }

        // Semnează un singur document ca admin (pentru bulk progres)
        public async Task SignSingleDocumentAsAdminAsync(UserDocument doc, string signatureMethod, string signatureData, string ipAddress)
        {
            var timestamp = DateTime.UtcNow;
            var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
            var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

            // Doar semnătură ca verifier (admin)
            doc.Status = doc.UserSignedAt != null ? "PendingManager" : "PendingUser";


            var latestTraining = doc.User?.PeriodicTrainings
                ?.Where(pt => pt.UserDocumentId == doc.Id)
                .OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();
            if (latestTraining != null)
            {
                latestTraining.VerifierSignature = signatureData;
                latestTraining.VerifierSignatureMethod = signatureMethod;
            }

            await _context.SaveChangesAsync();

            // Reîncarcă user fresh cu toate datele după save
            var freshUser = await _context.Users
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .FirstOrDefaultAsync(u => u.Id == doc.UserId);

            if (freshUser != null)
            {
                try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                catch { /* non-fatal */ }
            }
            await _context.SaveChangesAsync();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string F(string? val) => val?.Trim() is { Length: > 0 } v ? v : "—";
        private static string FDate(DateTime? dt) => dt.HasValue ? dt.Value.ToString("dd.MM.yyyy") : "___________";
        private static string FUnderline(string? val) => val?.Trim() is { Length: > 0 } v ? v : "___________";

        private static void SignatureRow(ColumnDescriptor col, bool isSsm,
            string? userSigMethod = null, string? userSigData = null,
            string? instructorSigMethod = null, string? instructorSigData = null,
            string? verifierSigMethod = null, string? verifierSigData = null)
        {
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(isSsm ? "Semnătura celui instruit:" : "Semnătura persoanei instruite:").FontSize(8);
                    if (!string.IsNullOrWhiteSpace(userSigData))
                        RenderSignature(c, userSigMethod, userSigData);
                    else
                        c.Item().PaddingTop(20).BorderBottom(0.5f).Text("");
                });
                row.ConstantItem(10);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Semnătura celui care a efectuat instruirea:").FontSize(8);
                    if (!string.IsNullOrWhiteSpace(instructorSigData))
                        RenderSignature(c, instructorSigMethod, instructorSigData);
                    else
                        c.Item().PaddingTop(20).BorderBottom(0.5f).Text("");
                });
                if (isSsm)
                {
                    row.ConstantItem(10);
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Semnătura celui care a verificat:").FontSize(8);
                        // DEBUG LOG: Verifier signature info
                        Console.WriteLine($"[PDF] VerifierSignatureMethod: {verifierSigMethod}");
                        Console.WriteLine($"[PDF] VerifierSignatureData: {(verifierSigData != null ? verifierSigData.Substring(0, Math.Min(100, verifierSigData.Length)) : "null")}");
                        if (!string.IsNullOrWhiteSpace(verifierSigData))
                            RenderSignature(c, verifierSigMethod, verifierSigData);
                        else
                            c.Item().PaddingTop(20).BorderBottom(0.5f).Text("");
                    });
                }
            });
        }

        private static void SectionHeader(ColumnDescriptor col, string title, string color)
        {
            col.Item().Background(color).Padding(5).Text(title).Bold().FontSize(11);
            col.Item().Height(6);
        }

        private static byte[]? TryDecodeSignature(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            try
            {
                var clean = data
                    .Replace("data:image/png;base64,", "")
                    .Replace("data:image/jpeg;base64,", "")
                    .Replace("data:image/svg+xml;base64,", "");
                return Convert.FromBase64String(clean);
            }
            catch { return null; }
        }

        // Renders a signature into a column descriptor item — image for Draw, text for Type.
        // Uses content detection rather than method parameter to avoid rendering errors when
        // verifier method is inferred from a different signer's method (e.g. instructor typed,
        // admin drew → without content detection the admin's base64 PNG would appear as text).
        private static void RenderSignature(ColumnDescriptor c, string? method, string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            // A drawn signature always starts with "data:image/"; a typed name never does.
            if (data.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var imgBytes = TryDecodeSignature(data);
                if (imgBytes != null) c.Item().Image(imgBytes).FitWidth();
            }
            else
            {
                c.Item().PaddingTop(2).AlignCenter().Text(data).FontSize(9).Italic();
            }
        }

        // ─── Core PDF builder ────────────────────────────────────────────────────

        private QuestPDF.Infrastructure.IDocument BuildDocument(User user, UserDocument document)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            bool isSsm = document.DocumentType?.ToUpper() == "SSM";
            string formTitle = isSsm
                ? "FIȘA DE SECURITATE ȘI SĂNĂTATE ÎN MUNCĂ"
                : "FIȘA DE INSTRUCTAJ PRIVIND SECURITATEA LA INCENDII (SU)";
            string accentColor = isSsm ? Colors.Blue.Lighten3 : Colors.Red.Lighten3;
            string headerColor = isSsm ? Colors.Blue.Darken2 : Colors.Red.Darken2;
            string coverBg = isSsm ? Colors.White : "#fff5f5";

            string managerName = user.AssignedTo != null
                ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}"
                : F(user.AdmittedByName);
            string managerFunction = user.AssignedTo?.Function?.Name ?? F(user.AdmittedByFunction);

            // Get the periodic training for this specific document (avoid mixing SSM/SU signatures)
            var latestPt = user.PeriodicTrainings?
                .Where(pt => pt.UserDocumentId == document.Id)
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();

            return QuestPDF.Fluent.Document.Create(container =>
            {
                // ══════════════════════════════════════════════════════
                // PAGE 1 — COVER (coperta)
                // ══════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(coverBg);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().AlignCenter()
                            .Text(formTitle)
                            .Bold().FontSize(13).FontColor(headerColor);

                        col.Item().Height(24);

                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(14).Column(info =>
                        {
                            void Row(string lbl, string val)
                            {
                                info.Item().Row(r =>
                                {
                                    r.ConstantItem(130).Text(lbl).Bold().FontSize(10);
                                    r.RelativeItem().BorderBottom(0.5f).Text(val).FontSize(10);
                                });
                                info.Item().Height(8);
                            }

                            Row("Unitatea:", F(user.Department?.Name));
                            Row("Numele și prenumele:", $"{user.FirstName} {user.LastName}");

                            if (isSsm)
                            {
                                Row("Domiciliul:", F(user.Address));
                                Row("Grupa sanguină:", F(user.BloodGroup));
                                Row("Legitimația / Marca:", F(user.BadgeNumber));
                            }
                            else
                            {
                                Row("Locul de muncă:", F(user.Department?.Name));
                                Row("Marca:", F(user.BadgeNumber));
                                Row("Domiciliul:", F(user.Address));
                            }
                        });

                        col.Item().Height(20);

                        col.Item().AlignCenter().Text($"Document generat: {document.GeneratedAt:dd.MM.yyyy}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Pag. "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
                    });
                });

                // ══════════════════════════════════════════════════════
                // PAGE 2 — DATE GENERALE + INSTRUIRE LA ANGAJARE
                // ══════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().Column(col =>
                    {
                        // ── Date Generale ──────────────────────────────
                        SectionHeader(col, "DATE GENERALE", accentColor);

                        col.Item().Column(data =>
                        {
                            void DataRow(string lbl, string val)
                            {
                                data.Item().Row(r =>
                                {
                                    r.ConstantItem(190).Text(lbl).Bold();
                                    r.RelativeItem().BorderBottom(0.5f).Text(val);
                                });
                                data.Item().Height(5);
                            }

                            DataRow("Nume, prenume:", $"{user.FirstName} {user.LastName}");
                            DataRow("Data și locul nașterii:", $"{FDate(user.DateOfBirth)}, {F(user.PlaceOfBirth)}");

                            if (isSsm)
                                DataRow("Calificarea:", F(user.Education));
                            else
                            {
                                DataRow("Studii:", F(user.Education));
                                DataRow("Calificarea (specialitatea, meseria):", F(user.Function?.Name));
                            }

                            DataRow("Funcția:", F(user.Function?.Name));
                            DataRow("Locul de muncă:", F(user.Department?.Name));

                            if (isSsm)
                            {
                                DataRow("Autorizații (ISCIR etc.):", F(user.Qualifications));
                                DataRow("Traseul și durata deplasare la/de la serviciu:",
                                    $"{F(user.CommuteRoute)}{(user.CommuteDurationMinutes.HasValue ? $" ({user.CommuteDurationMinutes} min)" : "")}");
                            }
                        });

                        col.Item().Height(8);

                        // ── Instruire la angajare ──────────────────────
                        string sectionTitle = isSsm ? "INSTRUIRE LA ANGAJARE" : "INSTRUCTAJUL LA ANGAJARE";
                        SectionHeader(col, sectionTitle, accentColor);

                        // 1. Instruire introductivă generală
                        string t1 = isSsm ? "1. Instruirea introductiv generală" : "1. Instructajul introductiv general";
                        col.Item().Text(t1).Bold();
                        col.Item().Height(3);
                        col.Item().Text(text =>
                        {
                            string verb = isSsm ? "efectuată" : "efectuat";
                            text.Span($"a fost {verb} la data ").FontSize(10);
                            text.Span(FUnderline((user.IntroductoryTrainingDate ?? latestPt?.TrainingDate)?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                            text.Span(" timp de ").FontSize(10);
                            text.Span(FUnderline((user.IntroductoryTrainingHours ?? (int?)latestPt?.DurationHours)?.ToString())).Underline().FontSize(10);
                            text.Span(" ore de către ").FontSize(10);
                            text.Span(FUnderline(user.IntroductoryTrainingInstructor ?? latestPt?.InstructorName ?? managerName)).Underline().FontSize(10);
                            text.Span(" având funcția de ").FontSize(10);
                            text.Span(FUnderline(user.IntroductoryTrainingInstructorFunction ?? managerFunction)).Underline().FontSize(10);
                        });
                        col.Item().Height(3);
                        col.Item().Text("Conținutul instruirii:").Bold();
                        var introContent = user.IntroductoryTrainingContent ?? latestPt?.MaterialTaught;
                        col.Item().Border(0.5f).Padding(6)
                            .Text(string.IsNullOrWhiteSpace(introContent) ? " " : introContent).FontSize(10);
                        var introVerifierSigData = isSsm ? latestPt?.VerifierSignature : null;
                        var introVerifierSigMethod = (isSsm && !string.IsNullOrEmpty(introVerifierSigData))
                            ? latestPt?.VerifierSignatureMethod
                            : null;
                        var introInstructorSigData = isSsm
                            ? (latestPt?.InstructorSignature ?? document.ManagerSignatureData)
                            : document.ManagerSignatureData;
                        var introInstructorSigMethod = isSsm
                            ? (latestPt?.InstructorSignatureMethod ?? document.ManagerSignatureMethod)
                            : document.ManagerSignatureMethod;

                        SignatureRow(col, isSsm,
                            document.UserSignatureMethod, document.UserSignatureData,
                            introInstructorSigMethod, introInstructorSigData,
                            introVerifierSigMethod, introVerifierSigData);

                        col.Item().Height(8);

                        // 2. Instruire la locul de muncă
                        string t2 = isSsm ? "2. Instruirea la locul de muncă" : "2. Instructajul la locul de muncă";
                        col.Item().Text(t2).Bold();
                        col.Item().Height(3);
                        col.Item().Text(text =>
                        {
                            string verb = isSsm ? "efectuată" : "efectuat";
                            text.Span($"a fost {verb} la data ").FontSize(10);
                            text.Span(FUnderline((user.WorkplaceTrainingDate ?? latestPt?.TrainingDate)?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                            text.Span(" loc de muncă/post de lucru ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingLocation ?? user.Function?.Name)).Underline().FontSize(10);
                            text.Span(" timp de ").FontSize(10);
                            text.Span(FUnderline((user.WorkplaceTrainingHours ?? (int?)latestPt?.DurationHours)?.ToString())).Underline().FontSize(10);
                            text.Span(" ore, de către ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingInstructor ?? latestPt?.InstructorName ?? managerName)).Underline().FontSize(10);
                            text.Span(" având funcția de ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingInstructorFunction ?? managerFunction)).Underline().FontSize(10);
                        });
                        col.Item().Height(3);
                        col.Item().Text("Conținutul instruirii:").Bold();
                        var workContent = user.WorkplaceTrainingContent ?? latestPt?.MaterialTaught;
                        col.Item().Border(0.5f).Padding(6)
                            .Text(string.IsNullOrWhiteSpace(workContent) ? " " : workContent).FontSize(10);
                        var workVerifierSigData = isSsm ? latestPt?.VerifierSignature : null;
                        var workVerifierSigMethod = (isSsm && !string.IsNullOrEmpty(workVerifierSigData))
                            ? latestPt?.VerifierSignatureMethod
                            : null;
                        var workInstructorSigData = isSsm
                            ? (latestPt?.InstructorSignature ?? document.ManagerSignatureData)
                            : document.ManagerSignatureData;
                        var workInstructorSigMethod = isSsm
                            ? (latestPt?.InstructorSignatureMethod ?? document.ManagerSignatureMethod)
                            : document.ManagerSignatureMethod;

                        SignatureRow(col, isSsm,
                            document.UserSignatureMethod, document.UserSignatureData,
                            workInstructorSigMethod, workInstructorSigData,
                            workVerifierSigMethod, workVerifierSigData);

                        col.Item().Height(10);

                        // 3. Admis la lucru
                        col.Item().Text("3. Admis la lucru").Bold();
                        col.Item().Height(3);
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(160).Text("Numele și prenumele:").Bold();
                            r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedByName ?? managerName));
                        });
                        col.Item().Height(4);
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(160).Text("Funcția (șef secție, atelier, șantier):").Bold();
                            r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedByFunction ?? managerFunction));
                        });
                        col.Item().Height(4);
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(160).Text("Data și semnătura:").Bold();
                            r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedDate?.ToString("dd.MM.yyyy")));
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Pag. "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
                    });
                });

                // ══════════════════════════════════════════════════════
                // PAGE 3 — INSTRUIRE PERIODICĂ (landscape table)
                // ══════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Content().Column(col =>
                    {
                        string periodicTitle = isSsm ? "3. INSTRUIRE PERIODICĂ" : "INSTRUCTAJUL PERIODIC";
                        SectionHeader(col, periodicTitle, accentColor);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60);   // Data
                                c.ConstantColumn(35);   // Durata
                                c.RelativeColumn(2);    // Ocupatia / Specialitatea
                                c.RelativeColumn(3);    // Material predat
                                c.RelativeColumn(1.5f);    // Semnătură instruit
                                c.RelativeColumn(1.5f);    // Semnătură instructor
                                if (isSsm) c.RelativeColumn(1.5f); // Semnătură verificator
                            });

                            static IContainer HeaderCell(IContainer c) =>
                                c.Background(Colors.Grey.Lighten2).Border(0.5f).Padding(2);

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Data instruirii").Bold().FontSize(7);
                                header.Cell().Element(HeaderCell).Text("Durata (h)").Bold().FontSize(7);
                                header.Cell().Element(HeaderCell).Text(isSsm ? "Ocupația" : "Specialitatea").Bold().FontSize(7);
                                header.Cell().Element(HeaderCell).Text("Materialul predat").Bold().FontSize(7);
                                header.Cell().Element(HeaderCell).Text("Semnătura\ninstruit").Bold().FontSize(7);
                                header.Cell().Element(HeaderCell).Text("Semnătura\ninstructor").Bold().FontSize(7);
                                if (isSsm)
                                    header.Cell().Element(HeaderCell).Text("Semnătura\nverificator").Bold().FontSize(7);
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.Border(0.5f).Padding(2).MinHeight(14);

                            static IContainer HighlightCell(IContainer c) =>
                                c.Background("#FFF9C4").Border(0.5f).Padding(3).MinHeight(16);

                            // Show periodic training records for this document only (no mixing with other document types)
                            var periodicTrainings = user.PeriodicTrainings?
                                .Where(pt => pt.UserDocumentId == document.Id)
                                .OrderBy(pt => pt.CreatedAt).ToList() ?? new List<PeriodicTraining>();
                            string occupation = user.Function?.Name ?? "";

                            bool hasTrainings = periodicTrainings.Count > 0;

                            for (int i = 0; i < periodicTrainings.Count; i++)
                            {
                                var training = periodicTrainings[i];
                                bool isLastRow = i == periodicTrainings.Count - 1;

                                    // Use only per-row signatures stored directly on the PeriodicTraining row.
                                    // No fallback to document-level fields — those are transient; the
                                    // canonical signature store is always the training row.
                                    string? userSigData   = training.UserSignatureData;
                                    string? userSigMethod = training.UserSignatureMethod;
                                    string? mgrSigData    = training.InstructorSignature;
                                    string? mgrSigMethod  = training.InstructorSignatureMethod;
                                    string? verifierSigData   = training.VerifierSignature;
                                    string? verifierSigMethod = !string.IsNullOrEmpty(verifierSigData) ? training.VerifierSignatureMethod : null;

                                    // For SSM, when verifier signature exists (admin), do not duplicate into instructor column.
                                if (isSsm && !string.IsNullOrEmpty(verifierSigData) && string.IsNullOrEmpty(training.InstructorSignature))
                                {
                                        mgrSigData   = null;
                                        mgrSigMethod = null;
                                }


                                // Evidențiere pentru cazurile:
                                // 1. Lipsesc ambele semnături (angajat și manager/verificator)
                                // 2. Există semnătura angajatului, dar lipsește cea a managerului/verificatorului (pentru ca managerul să vadă linia evidențiată)
                                bool missingSignature = isSsm
                                    ? string.IsNullOrEmpty(userSigData) || (string.IsNullOrEmpty(mgrSigData) && string.IsNullOrEmpty(verifierSigData))
                                    : string.IsNullOrEmpty(userSigData) || string.IsNullOrEmpty(mgrSigData);


                                bool highlightForManager = isSsm
                                    ? !string.IsNullOrEmpty(userSigData) && (string.IsNullOrEmpty(mgrSigData) || string.IsNullOrEmpty(verifierSigData))
                                    : !string.IsNullOrEmpty(userSigData) && string.IsNullOrEmpty(mgrSigData);

                                Func<IContainer, IContainer> rowCell = (isLastRow && (missingSignature || highlightForManager)) ? HighlightCell : DataCell;

                                table.Cell().Element(rowCell).Text(training.TrainingDate?.ToString("dd.MM.yyyy") ?? "").FontSize(7);
                                table.Cell().Element(rowCell).Text(training.DurationHours?.ToString("0.#") ?? "").FontSize(7);
                                table.Cell().Element(rowCell).Text(training.Occupation ?? occupation).FontSize(7);
                                table.Cell().Element(rowCell).Text(training.MaterialTaught ?? "").FontSize(7);

                                table.Cell().Element(rowCell).Column(c => RenderSignature(c, userSigMethod, userSigData));
                                table.Cell().Element(rowCell).Column(c => RenderSignature(c, mgrSigMethod, mgrSigData));

                                if (isSsm)
                                    table.Cell().Element(rowCell).Column(c => RenderSignature(c, verifierSigMethod, verifierSigData));
                            }

                            // Fallback: if no periodic trainings exist, render an empty highlighted row.
                            if (!hasTrainings)
                            {
                                Func<IContainer, IContainer> rowCell = HighlightCell;

                                table.Cell().Element(rowCell).Text(document.GeneratedAt.ToString("dd.MM.yyyy")).FontSize(7);
                                table.Cell().Element(rowCell).Text("").FontSize(7);
                                table.Cell().Element(rowCell).Text(occupation).FontSize(7);
                                table.Cell().Element(rowCell).Text("").FontSize(7);
                                table.Cell().Element(rowCell).Text(""); // employee sig — empty until signed
                                table.Cell().Element(rowCell).Text(""); // instructor sig — empty until signed
                                if (isSsm)
                                    table.Cell().Element(rowCell).Text(""); // verifier sig — empty until signed
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Pag. "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
                    });
                });
            });
        }

        // ─── Public interface methods ────────────────────────────────────────────

        public Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document)
        {
            var docsFolder = Path.Combine(Directory.GetCurrentDirectory(), "GeneratedDocuments");
            if (!Directory.Exists(docsFolder)) Directory.CreateDirectory(docsFolder);

            var fileName = $"{document.DocumentType}_{user.FirstName}_{user.LastName}_{document.Id}.pdf";
            var filePath = Path.Combine(docsFolder, fileName);

            // Generate to memory first — if layout throws, the existing file on disk is NOT corrupted
            var pdfBytes = BuildDocument(user, document).GeneratePdf();
            File.WriteAllBytes(filePath, pdfBytes);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(pdfBytes);
            document.DocumentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            return Task.FromResult(filePath);
        }

        public Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document)
        {
            var bytes = BuildDocument(user, document).GeneratePdf();
            return Task.FromResult(bytes);
        }

        public async Task<IEnumerable<UserDocument>> GetAllPendingUserDocumentsAsync(string documentType)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                .Where(d => d.DocumentType == documentType && d.Status == "PendingUser")
                .ToListAsync();
        }

        public async Task<UserDocument?> GetDocumentByIdAsync(Guid documentId)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        public async Task<IEnumerable<UserDocument>> GetUserDocumentsAsync(Guid userId)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<HashSet<Guid>> GetUserIdsWithDocumentTypeAsync(string documentType)
        {
            var ids = await _context.UserDocuments
                .Where(d => d.DocumentType == documentType)
                .Where(d => d.UserSignedAt != null || !string.IsNullOrEmpty(d.UserSignatureData))
                .Select(d => d.UserId)
                .Distinct()
                .ToListAsync();
            return new HashSet<Guid>(ids);
        }

        public async Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress, bool isAdminSignature = false)
        {
            var doc = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (doc == null) return false;

            var timestamp = DateTime.UtcNow;
            var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
            var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

            if (isUserSignature)
            {
                doc.UserSignatureMethod = signatureMethod;
                doc.UserSignatureData = signatureData;
                doc.UserSignatureIpAddress = ipAddress;
                doc.UserSignedAt = timestamp;
                doc.UserCryptographicSignature = cryptoSignature;
                doc.Status = doc.ManagerSignedAt != null ? "Completed" : "PendingManager";
            }
            else
            {
                bool isSsmAdminVerifier = isAdminSignature && doc.DocumentType?.ToUpperInvariant() == "SSM";
                if (isSsmAdminVerifier)
                {
                    // Admin SSM signature is verifier-only; line-manager countersignature remains pending.
                    doc.Status = doc.UserSignedAt != null ? "PendingManager" : "PendingUser";
                }
                else
                {
                    doc.ManagerSignatureMethod = signatureMethod;
                    doc.ManagerSignatureData = signatureData;
                    doc.ManagerSignatureIpAddress = ipAddress;
                    doc.ManagerSignedAt = timestamp;
                    doc.ManagerCryptographicSignature = cryptoSignature;
                    doc.Status = doc.UserSignedAt != null ? "Completed" : "PendingUser";
                }
            }

            // Persist signature to the PeriodicTraining row that belongs to this document
            var latestTraining = doc.User?.PeriodicTrainings
                ?.Where(pt => pt.UserDocumentId == documentId)
                .OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();

            if (latestTraining != null)
            {
                if (isUserSignature && string.IsNullOrEmpty(latestTraining.UserSignatureData))
                {
                    latestTraining.UserSignatureData = signatureData;
                    latestTraining.UserSignatureMethod = signatureMethod;
                }
                else if (!isUserSignature && doc.DocumentType?.ToUpperInvariant() == "SSM" && isAdminSignature)
                {
                    latestTraining.VerifierSignature = signatureData;
                    latestTraining.VerifierSignatureMethod = signatureMethod;
                }
                else if (!isUserSignature && string.IsNullOrEmpty(latestTraining.InstructorSignature))
                {
                    latestTraining.InstructorSignature = signatureData;
                    latestTraining.InstructorSignatureMethod = signatureMethod;
                }
            }

            // Regenerate PDF with embedded signature image
            if (doc.User != null && !string.IsNullOrEmpty(doc.PdfFilePath))
            {
                try
                {
                    await GeneratePdfSnapshotAsync(doc.User, doc);
                }
                catch { /* non-fatal: keep old PDF if regeneration fails */ }
            }

            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<int> BulkSignDocumentsAsync(bool isAdmin, Guid signerUserId, string signatureMethod, string signatureData, string ipAddress)
        {
            var docs = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Role)
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .Where(d =>
                    (isAdmin
                        ? d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" && d.User != null && (d.User.Role == null || d.User.Role.Name.ToUpper() != "ADMIN") && (d.Status == "PendingManager" || d.Status == "PendingUser") && d.ManagerSignedAt == null
                        : (d.Status == "PendingManager" || d.Status == "PendingUser") && d.ManagerSignedAt == null && d.User != null && d.User.AssignedToId == signerUserId)
                )
                .ToListAsync();

            if (docs.Count == 0) return 0;

            var timestamp = DateTime.UtcNow;

            foreach (var doc in docs)
            {
                var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
                var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

                bool isSsmAdminVerifier = isAdmin && doc.DocumentType?.ToUpperInvariant() == "SSM";
                if (isSsmAdminVerifier)
                {
                    doc.Status = doc.UserSignedAt != null ? "PendingManager" : "PendingUser";
                }
                else
                {
                    doc.ManagerSignatureMethod = signatureMethod;
                    doc.ManagerSignatureData = signatureData;
                    doc.ManagerSignatureIpAddress = ipAddress;
                    doc.ManagerSignedAt = timestamp;
                    doc.ManagerCryptographicSignature = cryptoSignature;
                    doc.Status = doc.UserSignedAt != null ? "Completed" : "PendingUser";
                }

                var trainingForDoc = await _context.PeriodicTrainings
                    .Where(pt => pt.UserDocumentId == doc.Id)
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();
                if (trainingForDoc != null)
                {
                    if (isSsmAdminVerifier)
                    {
                        trainingForDoc.VerifierSignature = signatureData;
                        trainingForDoc.VerifierSignatureMethod = signatureMethod;
                    }
                    else
                    {
                        trainingForDoc.InstructorSignature = signatureData;
                        trainingForDoc.InstructorSignatureMethod = signatureMethod;
                    }
                }
            }

            await _context.SaveChangesAsync();

            foreach (var doc in docs)
            {
                var freshUser = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(u => u.Department)
                    .Include(u => u.Function)
                    .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                    .FirstOrDefaultAsync(u => u.Id == doc.UserId);

                if (freshUser != null)
                {
                    try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                    catch { /* non-fatal */ }
                }
            }
            await _context.SaveChangesAsync();

            return isAdmin ? docs.Select(d => d.UserId).Distinct().Count() : docs.Count;
        }

        public async Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail)
        {
            bool isSsmDocumentType = string.Equals(documentType, "SSM", StringComparison.OrdinalIgnoreCase);

            var users = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .ToListAsync();

            // Filter out admin users on client side (EF doesn't support StringComparison parameter)
            var nonAdminUsers = users.Where(u => u.Role == null || !string.Equals(u.Role.Name, "Admin", StringComparison.OrdinalIgnoreCase)).ToList();

            int generated = 0;
            int skipped = 0;

            foreach (var user in nonAdminUsers)
            {
                try
                {
                    await GenerateDocumentAsync(user.Id, documentType, generatedByEmail);
                    generated++;
                }
                catch
                {
                    skipped++;
                }
            }

            return (generated, skipped);
        }

        public async Task<int> BulkSignAndSendGeneratedDocumentsAsync(string documentType, string signatureMethod, string signatureData, string ipAddress)
        {
            var docs = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Role)
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .Where(d => d.DocumentType == documentType && d.Status == "PendingUser" && d.ManagerSignedAt == null)
                .ToListAsync();

            if (docs.Count == 0) return 0;

            var timestamp = DateTime.UtcNow;

            foreach (var doc in docs)
            {
                var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
                var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

                bool isSsmAdminVerifier = doc.DocumentType?.ToUpperInvariant() == "SSM";
                if (isSsmAdminVerifier)
                {
                    doc.Status = doc.UserSignedAt != null ? "PendingManager" : "PendingUser";
                }
                else
                {
                    doc.ManagerSignatureMethod = signatureMethod;
                    doc.ManagerSignatureData = signatureData;
                    doc.ManagerSignatureIpAddress = ipAddress;
                    doc.ManagerSignedAt = timestamp;
                    doc.ManagerCryptographicSignature = cryptoSignature;
                    doc.Status = "PendingUser";
                }

                var trainingForDoc = doc.User?.PeriodicTrainings
                    ?.Where(pt => pt.UserDocumentId == doc.Id)
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefault();
                if (trainingForDoc != null)
                {
                    if (isSsmAdminVerifier)
                    {
                        trainingForDoc.VerifierSignature = signatureData;
                        trainingForDoc.VerifierSignatureMethod = signatureMethod;
                    }
                    else
                    {
                        trainingForDoc.InstructorSignature = signatureData;
                        trainingForDoc.InstructorSignatureMethod = signatureMethod;
                    }
                }
            }

            await _context.SaveChangesAsync();

            foreach (var doc in docs)
            {
                var freshUser = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                    .Include(u => u.Department)
                    .Include(u => u.Function)
                    .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                    .FirstOrDefaultAsync(u => u.Id == doc.UserId);

                if (freshUser != null)
                {
                    try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                    catch { /* non-fatal */ }
                }
            }
            await _context.SaveChangesAsync();

            return docs.Count;
        }
    }
}
