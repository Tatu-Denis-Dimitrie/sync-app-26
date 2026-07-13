using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
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
            return await _context.UserDocuments
                .Include(d => d.User)
                .CountAsync(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.User != null &&
                    d.User.Role != UserRole.Admin &&
                    d.Status == "PendingAdmin");
        }

        // Returnează lista de documente SSM ce trebuie semnate de admin (pentru bulk progres)
        // Returns ALL pending documents ordered oldest-first so signatures are applied in creation order.
        public async Task<List<UserDocument>> GetPendingSsmDocumentsForAdminListAsync()
        {
            var allPending = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.InitialTrainings)
                .Where(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.User != null &&
                    d.User.Role != UserRole.Admin &&
                    d.Status == "PendingAdmin")
                .ToListAsync();

            return allPending.OrderBy(d => d.GeneratedAt).ToList();
        }

        public async Task<UserDocument> GenerateDocumentAsync(Guid userId, string documentType, string generatedByEmail)
        {
            Console.WriteLine($"[GENERATE] Starting document generation for UserId: {userId}, DocumentType: {documentType}, GeneratedBy: {generatedByEmail}");

            await EnsureUserCanHaveDocumentGeneratedAsync(userId);

            var doc = await CreateUserDocumentAsync(userId, documentType);

            await CopyHistoricalPeriodicTrainingRowsAsync(userId, documentType, doc.Id);
            await LinkOrCreateCurrentPeriodicTrainingRowAsync(userId, documentType, doc.Id);
            await _context.SaveChangesAsync();

            var user = await LoadUserWithDocumentDataAsync(userId)
                ?? throw new ArgumentException("User not found.");

            var pdfPath = await GeneratePdfSnapshotAsync(user, doc);
            doc.PdfFilePath = pdfPath;
            await _context.SaveChangesAsync();

            return doc;
        }

        // Admins should not have SSM/SU documents generated for them.
        private async Task EnsureUserCanHaveDocumentGeneratedAsync(Guid userId)
        {
            var userToGenerate = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (userToGenerate != null && userToGenerate.Role == UserRole.Admin)
                throw new InvalidOperationException("Cannot generate documents for admin users.");
        }

        private async Task<UserDocument> CreateUserDocumentAsync(Guid userId, string documentType)
        {
            var doc = new UserDocument
            {
                UserId = userId,
                DocumentType = documentType ?? string.Empty,
                Status = "PendingUser",
                GeneratedAt = DateTime.UtcNow
            };
            _context.UserDocuments.Add(doc);
            await _context.SaveChangesAsync();
            return doc;
        }

        private async Task CopyHistoricalPeriodicTrainingRowsAsync(Guid userId, string documentType, Guid newDocId)
        {
            var allPreviousDocIds = await _context.UserDocuments
                .Where(d => d.UserId == userId && d.DocumentType == documentType && d.Id != newDocId)
                .Select(d => d.Id)
                .ToListAsync();

            var previousDocPtRows = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == userId
                    && pt.UserDocumentId != null
                    && allPreviousDocIds.Contains(pt.UserDocumentId.Value)
                    && (pt.DocumentType == null || pt.DocumentType == documentType))
                .ToListAsync();

            var contentRows = previousDocPtRows.Where(pt =>
                !string.IsNullOrEmpty(pt.MaterialTaught)
                || !string.IsNullOrEmpty(pt.UserSignatureData)
                || !string.IsNullOrEmpty(pt.InstructorSignature)
                || !string.IsNullOrEmpty(pt.VerifierSignature))
                .ToList();

            var bestRows = SelectBestPeriodicTrainingRows(contentRows);

            foreach (var oldRow in bestRows)
            {
                var sourceId = oldRow.SourceRowId ?? oldRow.Id;
                var copy = new PeriodicTraining
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    UserDocumentId = newDocId,
                    DocumentType = documentType,
                    SourceRowId = sourceId,
                    TrainingDate = oldRow.TrainingDate,
                    DurationHours = oldRow.DurationHours,
                    Occupation = oldRow.Occupation,
                    MaterialTaught = oldRow.MaterialTaught,
                    InstructorName = oldRow.InstructorName,
                    VerifierName = oldRow.VerifierName,
                    UserSignatureData = oldRow.UserSignatureData,
                    UserSignatureMethod = oldRow.UserSignatureMethod,
                    InstructorSignature = oldRow.InstructorSignature,
                    InstructorSignatureMethod = oldRow.InstructorSignatureMethod,
                    VerifierSignature = oldRow.VerifierSignature,
                    VerifierSignatureMethod = oldRow.VerifierSignatureMethod,
                    CreatedAt = oldRow.CreatedAt,
                };
                _context.PeriodicTrainings.Add(copy);
            }
        }

        private static List<PeriodicTraining> SelectBestPeriodicTrainingRows(List<PeriodicTraining> contentRows)
        {
            var referencedSourceIds = contentRows
                .Where(r => r.SourceRowId.HasValue)
                .Select(r => r.SourceRowId!.Value)
                .ToHashSet();

            return contentRows
                .GroupBy(pt => pt.SourceRowId.HasValue
                    ? pt.SourceRowId.Value.ToString()
                    : referencedSourceIds.Contains(pt.Id)
                        ? pt.Id.ToString()
                        : $"{pt.TrainingDate:O}|{pt.CreatedAt:O}|{pt.Occupation}|{pt.MaterialTaught}")
                .Select(g => g.OrderByDescending(pt =>
                    (!string.IsNullOrEmpty(pt.UserSignatureData) ? 1 : 0) +
                    (!string.IsNullOrEmpty(pt.InstructorSignature) ? 1 : 0) +
                    (!string.IsNullOrEmpty(pt.VerifierSignature) ? 1 : 0)).First())
                .OrderBy(pt => pt.CreatedAt)
                .ToList();
        }

        private async Task LinkOrCreateCurrentPeriodicTrainingRowAsync(Guid userId, string documentType, Guid newDocId)
        {
            var unlinkedRows = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == userId
                    && pt.UserDocumentId == null
                    && (pt.DocumentType == null || pt.DocumentType == documentType))
                .ToListAsync();
            foreach (var row in unlinkedRows)
            {
                row.UserDocumentId = newDocId;
                row.DocumentType = documentType;
            }

            if (unlinkedRows.Count > 0)
                return;

            var mostRecentTraining = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == userId && pt.UserDocumentId != newDocId
                    && (pt.DocumentType == null || pt.DocumentType == documentType))
                .OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefaultAsync();

            var now = DateTime.UtcNow;
            _context.PeriodicTrainings.Add(new PeriodicTraining
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserDocumentId = newDocId,
                DocumentType = documentType,
                TrainingDate = now,
                DurationHours = mostRecentTraining?.DurationHours,
                Occupation = mostRecentTraining?.Occupation,
                MaterialTaught = mostRecentTraining?.MaterialTaught,
                InstructorName = mostRecentTraining?.InstructorName,
                VerifierName = mostRecentTraining?.VerifierName,
                CreatedAt = now,
            });
        }

        // Reloads a user with every navigation BuildDocument needs, PeriodicTrainings in
        // deterministic order (used after mutating training rows, ahead of a PDF snapshot).
        private Task<User?> LoadUserWithDocumentDataAsync(Guid userId) =>
            _context.Users
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.InitialTrainings)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .FirstOrDefaultAsync(u => u.Id == userId);

        public async Task SignSingleDocumentAsAdminAsync(UserDocument doc, string signatureMethod, string signatureData, string ipAddress)
        {
            var timestamp = DateTime.UtcNow;
            var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
            var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

            // Admin signs last — mark as Completed
            doc.AdminSignatureMethod = signatureMethod;
            doc.AdminSignatureData = signatureData;
            doc.AdminSignatureIpAddress = ipAddress;
            doc.AdminSignedAt = timestamp;
            doc.AdminCryptographicSignature = cryptoSignature;
            doc.Status = "Completed";


            var latestTraining = doc.User?.PeriodicTrainings
                ?.Where(pt => pt.UserDocumentId == doc.Id)
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();
            if (latestTraining != null)
            {
                latestTraining.VerifierSignature = signatureData;
                latestTraining.VerifierSignatureMethod = signatureMethod;
            }

            // Store verifier signature in UserInitialTraining (first-time only, never overwritten)
            var initialTraining = doc.User?.InitialTrainings
                ?.FirstOrDefault(t => string.Equals(t.DocumentType, doc.DocumentType, StringComparison.OrdinalIgnoreCase));
            if (initialTraining != null && string.IsNullOrEmpty(initialTraining.VerifierSignatureData))
            {
                initialTraining.VerifierSignatureData = signatureData;
                initialTraining.VerifierSignatureMethod = signatureMethod;
            }

            await _context.SaveChangesAsync();

            // Propagate verifier signature to copies of this row in newer documents
            if (latestTraining != null)
                await PropagateSignatureToNewerDocumentsAsync(latestTraining);

            var freshUser = await LoadUserWithDocumentDataAsync(doc.UserId);

            if (freshUser != null)
            {
                try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                catch { /* non-fatal */ }
            }
            await _context.SaveChangesAsync();
        }

        // ─── Propagate signature to copies in newer documents ────────────────────
        // When a PT row on an older document gets signed, all copies of that row
        // in newer documents must be updated so the PDF snapshots stay consistent.
        private async Task PropagateSignatureToNewerDocumentsAsync(PeriodicTraining signedRow)
        {
            if (signedRow.UserDocumentId == null) return;

            var owningDoc = await _context.UserDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == signedRow.UserDocumentId.Value);
            if (owningDoc == null) return;

            // Find all documents of the same type for the same user created AFTER this one
            var newerDocIds = await _context.UserDocuments
                .Where(d => d.UserId == owningDoc.UserId
                    && d.DocumentType == owningDoc.DocumentType
                    && d.GeneratedAt > owningDoc.GeneratedAt)
                .Select(d => d.Id)
                .ToListAsync();

            if (newerDocIds.Count == 0) return;

            // The dedup key used when copying rows during generation
            var key = $"{signedRow.TrainingDate:O}|{signedRow.CreatedAt:O}|{signedRow.Occupation}|{signedRow.MaterialTaught}";

            // Find all copied PT rows in newer documents that match this key
            var copiedRows = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == owningDoc.UserId
                    && pt.UserDocumentId.HasValue
                    && newerDocIds.Contains(pt.UserDocumentId.Value))
                .ToListAsync();

            var updatedDocIds = new HashSet<Guid>();
            foreach (var copy in copiedRows)
            {
                var copyKey = $"{copy.TrainingDate:O}|{copy.CreatedAt:O}|{copy.Occupation}|{copy.MaterialTaught}";
                if (copyKey != key) continue;

                // Propagate whichever signature fields were set on the signed row
                if (!string.IsNullOrEmpty(signedRow.UserSignatureData))
                {
                    copy.UserSignatureData = signedRow.UserSignatureData;
                    copy.UserSignatureMethod = signedRow.UserSignatureMethod;
                }
                if (!string.IsNullOrEmpty(signedRow.InstructorSignature))
                {
                    copy.InstructorSignature = signedRow.InstructorSignature;
                    copy.InstructorSignatureMethod = signedRow.InstructorSignatureMethod;
                }
                if (!string.IsNullOrEmpty(signedRow.VerifierSignature))
                {
                    copy.VerifierSignature = signedRow.VerifierSignature;
                    copy.VerifierSignatureMethod = signedRow.VerifierSignatureMethod;
                }
                if (copy.UserDocumentId.HasValue)
                    updatedDocIds.Add(copy.UserDocumentId.Value);
            }

            await _context.SaveChangesAsync();

            // Regenerate PDFs for all affected newer documents
            foreach (var docId in updatedDocIds)
            {
                var doc = await _context.UserDocuments.FirstOrDefaultAsync(d => d.Id == docId);
                if (doc == null) continue;
                var freshUser = await LoadUserWithDocumentDataAsync(doc.UserId);
                if (freshUser != null)
                {
                    try { await GeneratePdfSnapshotAsync(freshUser, doc); } catch { }
                }
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

        // Shared per-document styling/values computed once and threaded through each page builder.
        private readonly record struct DocumentRenderContext(
            bool IsSsm,
            string FormTitle,
            string AccentColor,
            string HeaderColor,
            string CoverBg,
            string ManagerName,
            string ManagerFunction);

        private QuestPDF.Infrastructure.IDocument BuildDocument(User user, UserDocument document, bool viewerIsAdmin = false)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var ctx = CreateRenderContext(user, document);

            return QuestPDF.Fluent.Document.Create(container =>
            {
                BuildCoverPage(container, user, document, ctx);
                BuildGeneralInfoPage(container, user, ctx);
                BuildPeriodicTrainingPage(container, user, document, ctx, viewerIsAdmin);
            });
        }

        private static DocumentRenderContext CreateRenderContext(User user, UserDocument document)
        {
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

            return new DocumentRenderContext(isSsm, formTitle, accentColor, headerColor, coverBg, managerName, managerFunction);
        }

        private static void PageFooter(PageDescriptor page)
        {
            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Pag. "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
            });
        }

        // ══════════════════════════════════════════════════════
        // PAGE 1 — COVER (coperta)
        // ══════════════════════════════════════════════════════
        private static void BuildCoverPage(QuestPDF.Infrastructure.IDocumentContainer container, User user, UserDocument document, DocumentRenderContext ctx)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(ctx.CoverBg);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter()
                        .Text(ctx.FormTitle)
                        .Bold().FontSize(13).FontColor(ctx.HeaderColor);

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

                        if (ctx.IsSsm)
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

                PageFooter(page);
            });
        }

        // ══════════════════════════════════════════════════════
        // PAGE 2 — DATE GENERALE + INSTRUIRE LA ANGAJARE
        // ══════════════════════════════════════════════════════
        private static void BuildGeneralInfoPage(QuestPDF.Infrastructure.IDocumentContainer container, User user, DocumentRenderContext ctx)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    BuildGeneralDataSection(col, user, ctx);
                    col.Item().Height(8);
                    BuildInitialTrainingSection(col, user, ctx);
                });

                PageFooter(page);
            });
        }

        private static void BuildGeneralDataSection(ColumnDescriptor col, User user, DocumentRenderContext ctx)
        {
            SectionHeader(col, "DATE GENERALE", ctx.AccentColor);

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

                if (ctx.IsSsm)
                    DataRow("Calificarea:", F(user.Education));
                else
                {
                    DataRow("Studii:", F(user.Education));
                    DataRow("Calificarea (specialitatea, meseria):", F(user.Function?.Name));
                }

                DataRow("Funcția:", F(user.Function?.Name));
                DataRow("Locul de muncă:", F(user.Department?.Name));

                if (ctx.IsSsm)
                {
                    DataRow("Autorizații (ISCIR etc.):", F(user.Qualifications));
                    DataRow("Traseul și durata deplasare la/de la serviciu:",
                        $"{F(user.CommuteRoute)}{(user.CommuteDurationMinutes.HasValue ? $" ({user.CommuteDurationMinutes} min)" : "")}");
                }
            });
        }

        private static void BuildInitialTrainingSection(ColumnDescriptor col, User user, DocumentRenderContext ctx)
        {
            bool isSsm = ctx.IsSsm;
            var it = user.InitialTrainings?.FirstOrDefault(t => t.DocumentType == (isSsm ? "SSM" : "SU"));
            string sectionTitle = isSsm ? "INSTRUIRE LA ANGAJARE" : "INSTRUCTAJUL LA ANGAJARE";
            SectionHeader(col, sectionTitle, ctx.AccentColor);

            RenderIntroductoryTrainingItem(col, ctx, it);
            col.Item().Height(8);
            RenderWorkplaceTrainingItem(col, user, ctx, it);
            col.Item().Height(10);
            RenderAdmittedToWorkItem(col, user, ctx);
        }

        // 1. Instruire introductivă generală
        private static void RenderIntroductoryTrainingItem(ColumnDescriptor col, DocumentRenderContext ctx, UserInitialTraining? it)
        {
            bool isSsm = ctx.IsSsm;
            string t1 = isSsm ? "1. Instruirea introductiv generală" : "1. Instructajul introductiv general";
            col.Item().Text(t1).Bold();
            col.Item().Height(3);
            col.Item().Text(text =>
            {
                string verb = isSsm ? "efectuată" : "efectuat";
                text.Span($"a fost {verb} la data ").FontSize(10);
                text.Span(FUnderline(it?.IntroductoryTrainingDate?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                text.Span(" timp de ").FontSize(10);
                text.Span(FUnderline(it?.IntroductoryTrainingHours?.ToString())).Underline().FontSize(10);
                text.Span(" ore de către ").FontSize(10);
                text.Span(FUnderline(it?.IntroductoryTrainingInstructor ?? ctx.ManagerName)).Underline().FontSize(10);
                text.Span(" având funcția de ").FontSize(10);
                text.Span(FUnderline(it?.IntroductoryTrainingInstructorFunction ?? ctx.ManagerFunction)).Underline().FontSize(10);
            });
            col.Item().Height(3);
            col.Item().Text("Conținutul instruirii:").Bold();
            var introContent = it?.IntroductoryTrainingContent;
            col.Item().Border(0.5f).Padding(6)
                .Text(string.IsNullOrWhiteSpace(introContent) ? " " : introContent).FontSize(10);
            // Signatures frozen from first signing — stored on UserInitialTraining
            SignatureRow(col, isSsm,
                it?.UserSignatureMethod, it?.UserSignatureData,
                it?.InstructorSignatureMethod, it?.InstructorSignatureData,
                it?.VerifierSignatureMethod, it?.VerifierSignatureData);
        }

        // 2. Instruire la locul de muncă
        private static void RenderWorkplaceTrainingItem(ColumnDescriptor col, User user, DocumentRenderContext ctx, UserInitialTraining? it)
        {
            bool isSsm = ctx.IsSsm;
            string t2 = isSsm ? "2. Instruirea la locul de muncă" : "2. Instructajul la locul de muncă";
            col.Item().Text(t2).Bold();
            col.Item().Height(3);
            col.Item().Text(text =>
            {
                string verb = isSsm ? "efectuată" : "efectuat";
                text.Span($"a fost {verb} la data ").FontSize(10);
                text.Span(FUnderline(it?.WorkplaceTrainingDate?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                text.Span(" loc de muncă/post de lucru ").FontSize(10);
                text.Span(FUnderline(it?.WorkplaceTrainingLocation ?? user.Function?.Name)).Underline().FontSize(10);
                text.Span(" timp de ").FontSize(10);
                text.Span(FUnderline(it?.WorkplaceTrainingHours?.ToString())).Underline().FontSize(10);
                text.Span(" ore, de către ").FontSize(10);
                text.Span(FUnderline(it?.WorkplaceTrainingInstructor ?? ctx.ManagerName)).Underline().FontSize(10);
                text.Span(" având funcția de ").FontSize(10);
                text.Span(FUnderline(it?.WorkplaceTrainingInstructorFunction ?? ctx.ManagerFunction)).Underline().FontSize(10);
            });
            col.Item().Height(3);
            col.Item().Text("Conținutul instruirii:").Bold();
            var workContent = it?.WorkplaceTrainingContent;
            col.Item().Border(0.5f).Padding(6)
                .Text(string.IsNullOrWhiteSpace(workContent) ? " " : workContent).FontSize(10);
            // Signatures frozen from first signing — stored on UserInitialTraining
            SignatureRow(col, isSsm,
                it?.UserSignatureMethod, it?.UserSignatureData,
                it?.InstructorSignatureMethod, it?.InstructorSignatureData,
                it?.VerifierSignatureMethod, it?.VerifierSignatureData);
        }

        // 3. Admis la lucru
        private static void RenderAdmittedToWorkItem(ColumnDescriptor col, User user, DocumentRenderContext ctx)
        {
            col.Item().Text("3. Admis la lucru").Bold();
            col.Item().Height(3);
            col.Item().Row(r =>
            {
                r.ConstantItem(160).Text("Numele și prenumele:").Bold();
                r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedByName ?? ctx.ManagerName));
            });
            col.Item().Height(4);
            col.Item().Row(r =>
            {
                r.ConstantItem(160).Text("Funcția (șef secție, atelier, șantier):").Bold();
                r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedByFunction ?? ctx.ManagerFunction));
            });
            col.Item().Height(4);
            col.Item().Row(r =>
            {
                r.ConstantItem(160).Text("Data și semnătura:").Bold();
                r.RelativeItem().BorderBottom(0.5f).Text(FUnderline(user.AdmittedDate?.ToString("dd.MM.yyyy")));
            });
        }

        // ══════════════════════════════════════════════════════
        // PAGE 3 — INSTRUIRE PERIODICĂ
        // ══════════════════════════════════════════════════════
        private static void BuildPeriodicTrainingPage(QuestPDF.Infrastructure.IDocumentContainer container, User user, UserDocument document, DocumentRenderContext ctx, bool viewerIsAdmin)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Content().Column(col =>
                {
                    string periodicTitle = ctx.IsSsm ? "3. INSTRUIRE PERIODICĂ" : "INSTRUCTAJUL PERIODIC";
                    SectionHeader(col, periodicTitle, ctx.AccentColor);

                    col.Item().Table(table => BuildPeriodicTrainingTable(table, user, document, ctx, viewerIsAdmin));
                });

                PageFooter(page);
            });
        }

        private static IContainer PeriodicHeaderCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten2).Border(0.5f).Padding(2);

        private static IContainer PeriodicDataCell(IContainer c) =>
            c.Border(0.5f).Padding(2).MinHeight(14);

        private static IContainer PeriodicHighlightCell(IContainer c) =>
            c.Background("#FFF9C4").Border(0.5f).Padding(3).MinHeight(16);

        private static void BuildPeriodicTrainingTable(TableDescriptor table, User user, UserDocument document, DocumentRenderContext ctx, bool viewerIsAdmin)
        {
            bool isSsm = ctx.IsSsm;

            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(20);   // Nr. crt.
                c.ConstantColumn(50);   // Data
                c.ConstantColumn(35);   // Durata
                c.RelativeColumn(1.0f); // Ocupatia / Specialitatea
                c.RelativeColumn(4.5f); // Material predat
                c.RelativeColumn(1.0f); // Semnătură instruit
                c.RelativeColumn(1.0f); // Semnătură instructor
                if (isSsm) c.RelativeColumn(1.0f); // Semnătură verificator
            });

            table.Header(header =>
            {
                header.Cell().Element(PeriodicHeaderCell).Text("Nr. crt.").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text("Data instruirii").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text("Durata (h)").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text(isSsm ? "Ocupația" : "Specialitatea").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text("Materialul predat").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text("Semnătura\ninstruit").Bold().FontSize(7);
                header.Cell().Element(PeriodicHeaderCell).Text("Semnătura\ninstructor").Bold().FontSize(7);
                if (isSsm)
                    header.Cell().Element(PeriodicHeaderCell).Text("Semnătura\nverificator").Bold().FontSize(7);
            });

            // Each document is self-contained: show only its own PT rows.
            // Order by CreatedAt: copies inherit CreatedAt from the original row,
            // so insertion order is preserved regardless of TrainingDate.
            // The current row (Step 2/3) always has the latest CreatedAt → naturally last.
            var periodicTrainings = (user.PeriodicTrainings?
                .Where(pt => pt.UserDocumentId == document.Id)
                .OrderBy(pt => pt.CreatedAt)
                .ToList()) ?? new List<PeriodicTraining>();
            string occupation = user.Function?.Name ?? "";

            for (int i = 0; i < periodicTrainings.Count; i++)
            {
                // The last row is the current (new) one; earlier rows are historical copies
                bool isCurrentDocRow = (i == periodicTrainings.Count - 1);
                RenderPeriodicTrainingRow(table, periodicTrainings[i], i, isCurrentDocRow, occupation, isSsm, viewerIsAdmin);
            }

            // Fallback: if no periodic trainings exist, render an empty row (highlighted for non-admin)
            if (periodicTrainings.Count == 0)
                RenderEmptyPeriodicTrainingRow(table, document, occupation, isSsm, viewerIsAdmin);
        }

        private static void RenderPeriodicTrainingRow(TableDescriptor table, PeriodicTraining training, int index, bool isCurrentDocRow, string occupation, bool isSsm, bool viewerIsAdmin)
        {
            // Use only per-row signatures stored directly on the PeriodicTraining row.
            // No fallback to document-level fields — those are transient; the
            // canonical signature store is always the training row.
            string? userSigData = training.UserSignatureData;
            string? userSigMethod = training.UserSignatureMethod;
            string? mgrSigData = training.InstructorSignature;
            string? mgrSigMethod = training.InstructorSignatureMethod;
            string? verifierSigData = training.VerifierSignature;
            string? verifierSigMethod = !string.IsNullOrEmpty(verifierSigData) ? training.VerifierSignatureMethod : null;

            // For SSM, when verifier signature exists (admin), do not duplicate into instructor column.
            if (isSsm && !string.IsNullOrEmpty(verifierSigData) && string.IsNullOrEmpty(training.InstructorSignature))
            {
                mgrSigData = null;
                mgrSigMethod = null;
            }

            // Highlight this row only if signatures are still missing and viewer is not admin
            bool allSigned = isSsm
                ? !string.IsNullOrEmpty(userSigData) && !string.IsNullOrEmpty(mgrSigData) && !string.IsNullOrEmpty(verifierSigData)
                : !string.IsNullOrEmpty(userSigData) && !string.IsNullOrEmpty(mgrSigData);

            Func<IContainer, IContainer> rowCell = (isCurrentDocRow && !allSigned && !viewerIsAdmin) ? PeriodicHighlightCell : PeriodicDataCell;

            table.Cell().Element(rowCell).Text((index + 1).ToString()).FontSize(7);
            table.Cell().Element(rowCell).Text(training.TrainingDate?.ToString("dd.MM.yyyy") ?? "").FontSize(7);
            table.Cell().Element(rowCell).Text(training.DurationHours?.ToString("0.#") ?? "").FontSize(7);
            table.Cell().Element(rowCell).Text(training.Occupation ?? occupation).FontSize(7);
            table.Cell().Element(rowCell).Text(training.MaterialTaught ?? "").FontSize(6.5f);

            table.Cell().Element(rowCell).Column(c => RenderSignature(c, userSigMethod, userSigData));
            table.Cell().Element(rowCell).Column(c => RenderSignature(c, mgrSigMethod, mgrSigData));

            if (isSsm)
                table.Cell().Element(rowCell).Column(c => RenderSignature(c, verifierSigMethod, verifierSigData));
        }

        private static void RenderEmptyPeriodicTrainingRow(TableDescriptor table, UserDocument document, string occupation, bool isSsm, bool viewerIsAdmin)
        {
            Func<IContainer, IContainer> rowCell = viewerIsAdmin ? PeriodicDataCell : PeriodicHighlightCell;

            table.Cell().Element(rowCell).Text("1").FontSize(7);
            table.Cell().Element(rowCell).Text(document.GeneratedAt.ToString("dd.MM.yyyy")).FontSize(7);
            table.Cell().Element(rowCell).Text("").FontSize(7);
            table.Cell().Element(rowCell).Text(occupation).FontSize(7);
            table.Cell().Element(rowCell).Text("").FontSize(7);
            table.Cell().Element(rowCell).Text(""); // employee sig — empty until signed
            table.Cell().Element(rowCell).Text(""); // instructor sig — empty until signed
            if (isSsm)
                table.Cell().Element(rowCell).Text(""); // verifier sig — empty until signed
        }

        // ─── Public interface methods ────────────────────────────────────────────

        public Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document)
        {
            var docsFolder = Path.Combine(Directory.GetCurrentDirectory(), "GeneratedDocuments");
            if (!Directory.Exists(docsFolder)) Directory.CreateDirectory(docsFolder);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"{timestamp}_{document.DocumentType}_{user.FirstName}_{user.LastName}_{document.Id}.pdf";
            var filePath = Path.Combine(docsFolder, fileName);

            // Generate to memory first — if layout throws, the existing file on disk is NOT corrupted
            var pdfBytes = BuildDocument(user, document).GeneratePdf();
            File.WriteAllBytes(filePath, pdfBytes);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(pdfBytes);
            document.DocumentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            return Task.FromResult(filePath);
        }

        public Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document, bool viewerIsAdmin = false)
        {
            var bytes = BuildDocument(user, document, viewerIsAdmin).GeneratePdf();
            return Task.FromResult(bytes);
        }


        public async Task<IEnumerable<UserDocument>> GetAllDocumentsAsync()
        {
            var all = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();

            return all.Where(d => d.User == null || d.User.Role != UserRole.Admin);
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
                    .ThenInclude(u => u.InitialTrainings)
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
                    .ThenInclude(u => u.InitialTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<UserDocument>> GetManagerPendingSignaturesAsync(Guid managerId)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .Include(d => d.User)
                    .ThenInclude(u => u.InitialTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Where(d => d.User != null && d.User.AssignedToId == managerId
                    && d.Status == "PendingManager"
                    && d.UserSignedAt != null
                    && d.ManagerSignedAt == null)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<UserDocument>> GetManagerSignedDocumentsAsync(Guid managerId)
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .Include(d => d.User)
                    .ThenInclude(u => u.InitialTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings)
                .Where(d => d.User != null && d.User.AssignedToId == managerId
                    && d.ManagerSignedAt != null)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<HashSet<Guid>> GetUserIdsWithDocumentTypeAsync(string documentType)
        {
            // Only count a user as "signed" if their LATEST document of this type has been signed by the user
            var latestPerUser = await _context.UserDocuments
                .Where(d => d.DocumentType == documentType)
                .GroupBy(d => d.UserId)
                .Select(g => g.OrderByDescending(d => d.GeneratedAt).First())
                .ToListAsync();

            var ids = latestPerUser
                .Where(d => d.UserSignedAt != null || !string.IsNullOrEmpty(d.UserSignatureData))
                .Select(d => d.UserId)
                .Distinct();
            return new HashSet<Guid>(ids);
        }

        public async Task<HashSet<Guid>> GetUserIdsWithUnsignedDocumentTypeAsync(string documentType)
        {
            // Only flag a user as "having unsigned" if their LATEST document of this type is PendingUser
            var latestPerUser = await _context.UserDocuments
                .Where(d => d.DocumentType == documentType)
                .GroupBy(d => d.UserId)
                .Select(g => g.OrderByDescending(d => d.GeneratedAt).First())
                .ToListAsync();

            var ids = latestPerUser
                .Where(d => d.Status == "PendingUser")
                .Select(d => d.UserId)
                .Distinct();
            return new HashSet<Guid>(ids);
        }

        public async Task<Guid?> GetCurrentTrainingIdForDocumentAsync(Guid documentId)
        {
            return await _context.PeriodicTrainings
                .Where(pt => pt.UserDocumentId == documentId)
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.CreatedAt)
                .Select(pt => (Guid?)pt.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress, bool isAdminSignature = false, Guid? periodicTrainingId = null)
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
                    .ThenInclude(u => u.InitialTrainings)
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
                doc.Status = "PendingManager";
            }
            else
            {
                bool isSsmAdminVerifier = isAdminSignature && doc.DocumentType?.ToUpperInvariant() == "SSM";
                if (isSsmAdminVerifier)
                {
                    // Admin signs last on SSM — store admin signature and mark as Completed
                    doc.AdminSignatureMethod = signatureMethod;
                    doc.AdminSignatureData = signatureData;
                    doc.AdminSignatureIpAddress = ipAddress;
                    doc.AdminSignedAt = timestamp;
                    doc.AdminCryptographicSignature = cryptoSignature;
                    doc.Status = "Completed";
                }
                else
                {
                    doc.ManagerSignatureMethod = signatureMethod;
                    doc.ManagerSignatureData = signatureData;
                    doc.ManagerSignatureIpAddress = ipAddress;
                    doc.ManagerSignedAt = timestamp;
                    doc.ManagerCryptographicSignature = cryptoSignature;
                    // SSM needs admin verifier next; SU is complete after LM signs
                    bool isSsm = doc.DocumentType?.ToUpperInvariant() == "SSM";
                    doc.Status = isSsm ? "PendingAdmin" : "Completed";
                }
            }

            // Persist signature to the specific PeriodicTraining row (by ID from token, or latest as fallback)
            var latestTraining = periodicTrainingId.HasValue
                ? doc.User?.PeriodicTrainings?.FirstOrDefault(pt => pt.Id == periodicTrainingId.Value && pt.UserDocumentId == documentId)
                  ?? doc.User?.PeriodicTrainings?.Where(pt => pt.UserDocumentId == documentId)
                      .OrderByDescending(pt => pt.TrainingDate).ThenByDescending(pt => pt.CreatedAt).FirstOrDefault()
                : doc.User?.PeriodicTrainings
                    ?.Where(pt => pt.UserDocumentId == documentId)
                    .OrderByDescending(pt => pt.TrainingDate)
                    .ThenByDescending(pt => pt.CreatedAt)
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

            // Capture signature into UserInitialTraining once (first-time only, never overwritten).
            // If no record exists yet and this is the first document for this user+type, create it
            // so the page-2 signature boxes are populated from the first row and then frozen.
            var initialTraining = doc.User?.InitialTrainings
                ?.FirstOrDefault(t => string.Equals(t.DocumentType, doc.DocumentType, StringComparison.OrdinalIgnoreCase));

            if (initialTraining == null && doc.User != null)
            {
                bool isFirstDocument = !await _context.UserDocuments
                    .AnyAsync(d => d.UserId == doc.UserId && d.DocumentType == doc.DocumentType && d.Id != doc.Id);
                if (isFirstDocument)
                {
                    initialTraining = new UserInitialTraining
                    {
                        UserId = doc.UserId,
                        DocumentType = doc.DocumentType ?? string.Empty,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UserInitialTrainings.Add(initialTraining);
                    doc.User.InitialTrainings.Add(initialTraining);
                }
            }

            if (initialTraining != null)
            {
                if (isUserSignature && string.IsNullOrEmpty(initialTraining.UserSignatureData))
                {
                    initialTraining.UserSignatureData = signatureData;
                    initialTraining.UserSignatureMethod = signatureMethod;
                }
                else if (!isUserSignature && isAdminSignature && doc.DocumentType?.ToUpperInvariant() == "SSM"
                    && string.IsNullOrEmpty(initialTraining.VerifierSignatureData))
                {
                    initialTraining.VerifierSignatureData = signatureData;
                    initialTraining.VerifierSignatureMethod = signatureMethod;
                }
                else if (!isUserSignature && !isAdminSignature && string.IsNullOrEmpty(initialTraining.InstructorSignatureData))
                {
                    initialTraining.InstructorSignatureData = signatureData;
                    initialTraining.InstructorSignatureMethod = signatureMethod;
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

            // Propagate the signature to copies of this row in all newer documents
            if (latestTraining != null)
            {
                await PropagateSignatureToNewerDocumentsAsync(latestTraining);
            }

            return true;
        }

        public async Task<int> BulkSignDocumentsAsync(bool isAdmin, Guid signerUserId, string signatureMethod, string signatureData, string ipAddress)
        {
            var allDocs = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.InitialTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .Where(d =>
                    (isAdmin
                        ? d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" && d.User != null && d.User.Role != UserRole.Admin && d.Status == "PendingAdmin"
                        : d.Status == "PendingManager" && d.UserSignedAt != null && d.ManagerSignedAt == null && d.User != null && d.User.AssignedToId == signerUserId)
                )
                .ToListAsync();

            // Process all pending documents ordered oldest-first so signatures are applied in creation order
            var docs = allDocs.OrderBy(d => d.GeneratedAt).ToList();

            if (docs.Count == 0) return 0;

            var timestamp = DateTime.UtcNow;

            foreach (var doc in docs)
            {
                var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
                var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

                bool isSsmAdminVerifier = isAdmin && doc.DocumentType?.ToUpperInvariant() == "SSM";
                if (isSsmAdminVerifier)
                {
                    // Admin signs last — store admin signature and mark as Completed
                    doc.AdminSignatureMethod = signatureMethod;
                    doc.AdminSignatureData = signatureData;
                    doc.AdminSignatureIpAddress = ipAddress;
                    doc.AdminSignedAt = timestamp;
                    doc.AdminCryptographicSignature = cryptoSignature;
                    doc.Status = "Completed";
                }
                else
                {
                    doc.ManagerSignatureMethod = signatureMethod;
                    doc.ManagerSignatureData = signatureData;
                    doc.ManagerSignatureIpAddress = ipAddress;
                    doc.ManagerSignedAt = timestamp;
                    doc.ManagerCryptographicSignature = cryptoSignature;
                    // SSM needs admin next; SU is complete after LM signs
                    bool isSsm = doc.DocumentType?.ToUpperInvariant() == "SSM";
                    doc.Status = isSsm ? "PendingAdmin" : "Completed";
                }

                var trainingForDoc = await _context.PeriodicTrainings
                    .Where(pt => pt.UserDocumentId == doc.Id)
                    .OrderByDescending(pt => pt.TrainingDate)
                    .ThenByDescending(pt => pt.CreatedAt)
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

                // Capture into UserInitialTraining (first-time only, never overwritten).
                // Create record automatically if missing and this is the first document.
                var initialTraining = doc.User?.InitialTrainings
                    ?.FirstOrDefault(t => string.Equals(t.DocumentType, doc.DocumentType, StringComparison.OrdinalIgnoreCase));

                if (initialTraining == null && doc.User != null)
                {
                    bool isFirstDocument = !await _context.UserDocuments
                        .AnyAsync(d => d.UserId == doc.UserId && d.DocumentType == doc.DocumentType && d.Id != doc.Id);
                    if (isFirstDocument)
                    {
                        initialTraining = new UserInitialTraining
                        {
                            UserId = doc.UserId,
                            DocumentType = doc.DocumentType ?? string.Empty,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.UserInitialTrainings.Add(initialTraining);
                        doc.User.InitialTrainings.Add(initialTraining);
                    }
                }

                if (initialTraining != null)
                {
                    if (isSsmAdminVerifier && string.IsNullOrEmpty(initialTraining.VerifierSignatureData))
                    {
                        initialTraining.VerifierSignatureData = signatureData;
                        initialTraining.VerifierSignatureMethod = signatureMethod;
                    }
                    else if (!isSsmAdminVerifier && string.IsNullOrEmpty(initialTraining.InstructorSignatureData))
                    {
                        initialTraining.InstructorSignatureData = signatureData;
                        initialTraining.InstructorSignatureMethod = signatureMethod;
                    }
                }
            }

            await _context.SaveChangesAsync();

            // Propagate signatures from older documents to their copies in newer documents,
            // then regenerate the PDF for each signed document.
            foreach (var doc in docs)
            {
                var signedTraining = await _context.PeriodicTrainings
                    .Where(pt => pt.UserDocumentId == doc.Id)
                    .OrderByDescending(pt => pt.TrainingDate)
                    .ThenByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();
                if (signedTraining != null)
                    await PropagateSignatureToNewerDocumentsAsync(signedTraining);

                var freshUser = await LoadUserWithDocumentDataAsync(doc.UserId);

                if (freshUser != null)
                {
                    try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                    catch { /* non-fatal */ }
                }
            }
            await _context.SaveChangesAsync();

            return isAdmin ? docs.Select(d => d.UserId).Distinct().Count() : docs.Count;
        }

        public async Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail, List<Guid>? selectedUserIds = null, Guid? restrictToAssignedToId = null)
        {
            bool isSsmDocumentType = string.Equals(documentType, "SSM", StringComparison.OrdinalIgnoreCase);

            var users = await _context.Users
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.InitialTrainings)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .ToListAsync();

            if (restrictToAssignedToId.HasValue)
            {
                var myEmployeeIds = users
                    .Where(u => u.AssignedToId == restrictToAssignedToId.Value)
                    .Select(u => u.Id)
                    .ToList();

                selectedUserIds = selectedUserIds == null || !selectedUserIds.Any()
                    ? myEmployeeIds
                    : selectedUserIds.Intersect(myEmployeeIds).ToList();
            }

            // Filter out admin users on client side (EF doesn't support StringComparison parameter)
            var nonAdminUsers = users
                .Where(u => u.Role != UserRole.Admin)
                .Where(u => selectedUserIds == null || selectedUserIds.Contains(u.Id))
                .ToList();

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
            var allDocs = await _context.UserDocuments
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

            // Only sign the LATEST document per user — old documents must not be touched
            var docs = allDocs
                .GroupBy(d => d.UserId)
                .Select(g => g.OrderByDescending(d => d.GeneratedAt).First())
                .ToList();

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
                    .OrderByDescending(pt => pt.TrainingDate)
                    .ThenByDescending(pt => pt.CreatedAt)
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
                var freshUser = await LoadUserWithDocumentDataAsync(doc.UserId);

                if (freshUser != null)
                {
                    try { await GeneratePdfSnapshotAsync(freshUser, doc); }
                    catch { /* non-fatal */ }
                }
            }
            await _context.SaveChangesAsync();

            return docs.Count;
        }

        // Returns SSM documents pending admin signature (PendingAdmin status, signed by both employee and LM)
        // Returns ALL pending documents (not just latest per user) so admin can sign older versions too.
        public async Task<List<UserDocument>> GetAdminPendingDocumentsAsync()
        {
            return await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                .Where(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.User != null &&
                    d.User.Role != UserRole.Admin &&
                    d.Status == "PendingAdmin")
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        // Returns SSM documents already signed by admin (Completed and have verifier signature)
        public async Task<List<UserDocument>> GetAdminSignedDocumentsAsync()
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
                .Where(d =>
                    d.DocumentType != null && d.DocumentType.ToUpper() == "SSM" &&
                    d.Status == "Completed" &&
                    d.User != null &&
                    d.User.Role != UserRole.Admin)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<int> RegenerateDocumentsAsync()
        {
            var docs = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.InitialTrainings)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt))
                .ToListAsync();

            int count = 0;
            foreach (var doc in docs)
            {
                if (doc.User == null) continue;
                try
                {
                    var pdfPath = await GeneratePdfSnapshotAsync(doc.User, doc);
                    doc.PdfFilePath = pdfPath;
                    count++;
                }
                catch { /* skip failed, continue with next */ }
            }

            await _context.SaveChangesAsync();
            return count;
        }

        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            var doc = await _context.UserDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId);
            if (doc == null) return false;

            // Unlink PeriodicTraining rows so they can be picked up by future document generation
            var linkedRows = await _context.PeriodicTrainings
                .Where(pt => pt.UserDocumentId == documentId)
                .ToListAsync();
            foreach (var row in linkedRows)
                row.UserDocumentId = null;

            // Delete PDF file if it exists
            if (!string.IsNullOrEmpty(doc.PdfFilePath) && File.Exists(doc.PdfFilePath))
            {
                try { File.Delete(doc.PdfFilePath); } catch { /* non-fatal */ }
            }

            _context.UserDocuments.Remove(doc);
            await _context.SaveChangesAsync();
            return true;
        }

    }
}
