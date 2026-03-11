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
                            || !string.IsNullOrEmpty(pt.InstructorSignature))
                        && (string.IsNullOrEmpty(pt.UserSignatureData)
                            || string.IsNullOrEmpty(pt.InstructorSignature)))
                    .OrderByDescending(pt => pt.CreatedAt)
                    .FirstOrDefaultAsync();

                if (rowToFinalize != null)
                {
                    if (string.IsNullOrEmpty(rowToFinalize.UserSignatureData))
                    {
                        rowToFinalize.UserSignatureData = existingDoc.UserSignatureData;
                        rowToFinalize.UserSignatureMethod = existingDoc.UserSignatureMethod;
                    }
                    if (string.IsNullOrEmpty(rowToFinalize.InstructorSignature))
                    {
                        rowToFinalize.InstructorSignature = existingDoc.ManagerSignatureData;
                        rowToFinalize.InstructorSignatureMethod = existingDoc.ManagerSignatureMethod;
                    }
                    await _context.SaveChangesAsync();
                }
            }

            // Now check if there's an incomplete PeriodicTraining row (missing at least one signature)
            var existingUnsignedTraining = await _context.PeriodicTrainings
                .Where(pt => pt.UserId == userId
                    && (string.IsNullOrEmpty(pt.UserSignatureData)
                        || string.IsNullOrEmpty(pt.InstructorSignature)))
                .OrderByDescending(pt => pt.DurationHours.HasValue ? 1 : 0)
                .ThenBy(pt => pt.CreatedAt)
                .FirstOrDefaultAsync();

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
                    CreatedAt = DateTime.UtcNow
                };
                _context.PeriodicTrainings.Add(newTraining);
            }
            // If unsigned row exists, we'll reuse it for signatures (no need to create a new one)

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
                    DocumentType = documentType,
                    Status = "PendingUser",
                    GeneratedAt = DateTime.UtcNow
                };
                _context.UserDocuments.Add(doc);
            }

            await _context.SaveChangesAsync();

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

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string F(string? val) => val?.Trim() is { Length: > 0 } v ? v : "—";
        private static string FDate(DateTime? dt) => dt.HasValue ? dt.Value.ToString("dd.MM.yyyy") : "___________";
        private static string FUnderline(string? val) => val?.Trim() is { Length: > 0 } v ? v : "___________";

        private static void SignatureRow(ColumnDescriptor col, bool isSsm,
            string? userSigMethod = null, string? userSigData = null,
            string? instructorSigMethod = null, string? instructorSigData = null)
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
        private static void RenderSignature(ColumnDescriptor c, string? method, string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            if (string.Equals(method, "Type", StringComparison.OrdinalIgnoreCase))
            {
                c.Item().PaddingTop(2).AlignCenter().Text(data).FontSize(9).Italic();
            }
            else
            {
                var imgBytes = TryDecodeSignature(data);
                if (imgBytes != null) c.Item().Image(imgBytes).FitWidth();
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

            // Get latest periodic training for fallback on training fields
            var latestPt = user.PeriodicTrainings?
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
                        SignatureRow(col, isSsm,
                            document.UserSignatureMethod, document.UserSignatureData,
                            document.ManagerSignatureMethod, document.ManagerSignatureData);

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
                        SignatureRow(col, isSsm,
                            document.UserSignatureMethod, document.UserSignatureData,
                            document.ManagerSignatureMethod, document.ManagerSignatureData);

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

                            // Show existing periodic training records only (no empty rows)
                            var periodicTrainings = user.PeriodicTrainings?.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt).ToList() ?? new List<PeriodicTraining>();
                            string occupation = user.Function?.Name ?? "";

                            bool hasTrainings = periodicTrainings.Count > 0;

                            for (int i = 0; i < periodicTrainings.Count; i++)
                            {
                                var training = periodicTrainings[i];
                                bool isLastRow = i == periodicTrainings.Count - 1;

                                // Use per-row signatures from PeriodicTraining; fall back to document-level for the last row
                                string? userSigData = !string.IsNullOrEmpty(training.UserSignatureData)
                                    ? training.UserSignatureData
                                    : (isLastRow ? document.UserSignatureData : null);
                                string? userSigMethod = !string.IsNullOrEmpty(training.UserSignatureMethod)
                                    ? training.UserSignatureMethod
                                    : (isLastRow ? document.UserSignatureMethod : null);
                                string? mgrSigData = !string.IsNullOrEmpty(training.InstructorSignature)
                                    ? training.InstructorSignature
                                    : (isLastRow ? document.ManagerSignatureData : null);
                                string? mgrSigMethod = !string.IsNullOrEmpty(training.InstructorSignatureMethod)
                                    ? training.InstructorSignatureMethod
                                    : (isLastRow ? document.ManagerSignatureMethod : null);

                                bool missingSignature = string.IsNullOrEmpty(userSigData) || string.IsNullOrEmpty(mgrSigData);
                                Func<IContainer, IContainer> rowCell = (isLastRow && missingSignature) ? HighlightCell : DataCell;

                                table.Cell().Element(rowCell).Text(training.TrainingDate?.ToString("dd.MM.yyyy") ?? "").FontSize(7);
                                table.Cell().Element(rowCell).Text(training.DurationHours?.ToString("0.#") ?? "").FontSize(7);
                                table.Cell().Element(rowCell).Text(training.Occupation ?? occupation).FontSize(7);
                                table.Cell().Element(rowCell).Text(training.MaterialTaught ?? "").FontSize(7);

                                table.Cell().Element(rowCell).Column(c => RenderSignature(c, userSigMethod, userSigData));
                                table.Cell().Element(rowCell).Column(c => RenderSignature(c, mgrSigMethod, mgrSigData));

                                if (isSsm) table.Cell().Element(rowCell).Text("");
                            }

                            // Fallback: if no periodic trainings exist, still render the signature row
                            if (!hasTrainings)
                            {
                                bool missingSignature = string.IsNullOrEmpty(document.UserSignatureData)
                                    || string.IsNullOrEmpty(document.ManagerSignatureData);
                                Func<IContainer, IContainer> rowCell = missingSignature ? HighlightCell : DataCell;

                                table.Cell().Element(rowCell).Text(document.GeneratedAt.ToString("dd.MM.yyyy")).FontSize(7);
                                table.Cell().Element(rowCell).Text("").FontSize(7);
                                table.Cell().Element(rowCell).Text(occupation).FontSize(7);
                                table.Cell().Element(rowCell).Text("").FontSize(7);

                                table.Cell().Element(rowCell).Column(c =>
                                    RenderSignature(c, document.UserSignatureMethod, document.UserSignatureData));

                                table.Cell().Element(rowCell).Column(c =>
                                    RenderSignature(c, document.ManagerSignatureMethod, document.ManagerSignatureData));

                                if (isSsm) table.Cell().Element(rowCell).Text("");
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
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.GeneratedAt)
                .ToListAsync();
        }

        public async Task<HashSet<Guid>> GetUserIdsWithDocumentTypeAsync(string documentType)
        {
            var ids = await _context.UserDocuments
                .Where(d => d.DocumentType == documentType)
                .Where(d => d.Status != "PendingUser" && d.Status != "PendingManager")
                .Select(d => d.UserId)
                .Distinct()
                .ToListAsync();
            return new HashSet<Guid>(ids);
        }

        public async Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress)
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
                doc.ManagerSignatureMethod = signatureMethod;
                doc.ManagerSignatureData = signatureData;
                doc.ManagerSignatureIpAddress = ipAddress;
                doc.ManagerSignedAt = timestamp;
                doc.ManagerCryptographicSignature = cryptoSignature;
                doc.Status = doc.UserSignedAt != null ? "Completed" : "PendingUser";
            }

            // Persist signature to the newest PeriodicTraining row (by CreatedAt) that still needs it
            var latestTraining = doc.User?.PeriodicTrainings
                ?.OrderByDescending(pt => pt.CreatedAt)
                .FirstOrDefault();

            if (latestTraining != null)
            {
                if (isUserSignature && string.IsNullOrEmpty(latestTraining.UserSignatureData))
                {
                    latestTraining.UserSignatureData = signatureData;
                    latestTraining.UserSignatureMethod = signatureMethod;
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
            var baseQuery = _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate));

            // Admins sign only PendingManager documents (existing behaviour).
            // Line managers can sign all their employees' documents regardless of whether the employee has signed yet.
            IQueryable<UserDocument> query = isAdmin
                ? baseQuery.Where(d => d.Status == "PendingManager" && d.ManagerSignedAt == null)
                : baseQuery.Where(d =>
                    (d.Status == "PendingManager" || d.Status == "PendingUser") &&
                    d.ManagerSignedAt == null &&
                    d.User != null && d.User.AssignedToId == signerUserId);

            var docs = await query.ToListAsync();
            if (docs.Count == 0) return 0;

            var timestamp = DateTime.UtcNow;

            foreach (var doc in docs)
            {
                var dataToSign = $"{doc.Id}|{doc.DocumentHash}|{ipAddress}|{timestamp:O}";
                var cryptoSignature = await _cryptographyService.SignDataAsync(dataToSign);

                doc.ManagerSignatureMethod = signatureMethod;
                doc.ManagerSignatureData = signatureData;
                doc.ManagerSignatureIpAddress = ipAddress;
                doc.ManagerSignedAt = timestamp;
                doc.ManagerCryptographicSignature = cryptoSignature;
                doc.Status = doc.UserSignedAt != null ? "Completed" : "PendingUser";

                var latestTraining = doc.User?.PeriodicTrainings
                    ?.OrderBy(pt => pt.TrainingDate).ThenBy(pt => pt.CreatedAt)
                    .LastOrDefault();
                if (latestTraining != null)
                {
                    latestTraining.InstructorSignature = signatureData;
                    latestTraining.InstructorSignatureMethod = signatureMethod;
                }
            }

            await _context.SaveChangesAsync();

            // Regenerate PDFs after committing signatures
            foreach (var doc in docs)
            {
                if (doc.User != null)
                {
                    try { await GeneratePdfSnapshotAsync(doc.User, doc); }
                    catch { /* non-fatal */ }
                }
            }
            await _context.SaveChangesAsync();

            return docs.Count;
        }

        public async Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail)
        {
            var users = await _context.Users
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                .ToListAsync();

            int generated = 0;
            int skipped = 0;

            foreach (var user in users)
            {
                try
                {
                    // Only create a new PeriodicTraining row if there's no existing unsigned one
                    var existingUnsigned = await _context.PeriodicTrainings
                        .Where(pt => pt.UserId == user.Id
                            && (string.IsNullOrEmpty(pt.UserSignatureData)
                                || string.IsNullOrEmpty(pt.InstructorSignature)))
                        .FirstOrDefaultAsync();

                    if (existingUnsigned == null)
                    {
                        var mostRecent = await _context.PeriodicTrainings
                            .Where(pt => pt.UserId == user.Id)
                            .OrderByDescending(pt => pt.CreatedAt)
                            .FirstOrDefaultAsync();

                        var newTraining = new PeriodicTraining
                        {
                            Id = Guid.NewGuid(),
                            UserId = user.Id,
                            TrainingDate = mostRecent?.TrainingDate ?? DateTime.UtcNow,
                            DurationHours = mostRecent?.DurationHours,
                            Occupation = mostRecent?.Occupation,
                            MaterialTaught = mostRecent?.MaterialTaught,
                            InstructorName = mostRecent?.InstructorName,
                            VerifierName = mostRecent?.VerifierName,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.PeriodicTrainings.Add(newTraining);
                    }

                    // Reuse or create the UserDocument
                    var doc = await _context.UserDocuments
                        .FirstOrDefaultAsync(d => d.UserId == user.Id && d.DocumentType == documentType);

                    if (doc != null)
                    {
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
                            UserId = user.Id,
                            DocumentType = documentType,
                            Status = "PendingUser",
                            GeneratedAt = DateTime.UtcNow
                        };
                        _context.UserDocuments.Add(doc);
                    }

                    await _context.SaveChangesAsync();

                    // Reload user with updated PeriodicTrainings
                    var fullUser = await _context.Users
                        .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                        .Include(u => u.Department)
                        .Include(u => u.Function)
                        .Include(u => u.PeriodicTrainings.OrderBy(pt => pt.TrainingDate))
                        .FirstOrDefaultAsync(u => u.Id == user.Id);

                    if (fullUser != null)
                    {
                        var pdfPath = await GeneratePdfSnapshotAsync(fullUser, doc);
                        doc.PdfFilePath = pdfPath;
                        await _context.SaveChangesAsync();
                    }

                    generated++;
                }
                catch
                {
                    skipped++;
                }
            }

            return (generated, skipped);
        }
    }
}
