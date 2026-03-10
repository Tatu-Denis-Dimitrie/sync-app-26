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
                .Include(u => u.AssignedTo).ThenInclude(m => m!.Function)
                .Include(u => u.Department)
                .Include(u => u.Function)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new ArgumentException("User not found.");

            var doc = new UserDocument
            {
                UserId = userId,
                DocumentType = documentType,
                Status = "PendingUser",
                GeneratedAt = DateTime.UtcNow
            };

            var pdfPath = await GeneratePdfSnapshotAsync(user, doc);
            doc.PdfFilePath = pdfPath;

            _context.UserDocuments.Add(doc);
            await _context.SaveChangesAsync();

            return doc;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string F(string? val) => val?.Trim() is { Length: > 0 } v ? v : "—";
        private static string FDate(DateTime? dt) => dt.HasValue ? dt.Value.ToString("dd.MM.yyyy") : "___________";
        private static string FUnderline(string? val) => val?.Trim() is { Length: > 0 } v ? v : "___________";

        private static void SignatureRow(ColumnDescriptor col, bool isSsm)
        {
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(isSsm ? "Semnătura celui instruit:" : "Semnătura persoanei instruite:").FontSize(8);
                    c.Item().PaddingTop(20).BorderBottom(0.5f).Text("");
                });
                row.ConstantItem(10);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Semnătura celui care a efectuat instruirea:").FontSize(8);
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
                            text.Span(FUnderline(user.IntroductoryTrainingDate?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                            text.Span(" timp de ").FontSize(10);
                            text.Span(FUnderline(user.IntroductoryTrainingHours?.ToString())).Underline().FontSize(10);
                            text.Span(" ore de către ").FontSize(10);
                            text.Span(FUnderline(user.IntroductoryTrainingInstructor ?? managerName)).Underline().FontSize(10);
                            text.Span(" având funcția de ").FontSize(10);
                            text.Span(FUnderline(user.IntroductoryTrainingInstructorFunction ?? managerFunction)).Underline().FontSize(10);
                        });
                        col.Item().Height(3);
                        col.Item().Text("Conținutul instruirii:").Bold();
                        col.Item().Border(0.5f).Padding(6)
                            .Text(string.IsNullOrWhiteSpace(user.IntroductoryTrainingContent) ? " " : user.IntroductoryTrainingContent).FontSize(10);
                        SignatureRow(col, isSsm);

                        col.Item().Height(8);

                        // 2. Instruire la locul de muncă
                        string t2 = isSsm ? "2. Instruirea la locul de muncă" : "2. Instructajul la locul de muncă";
                        col.Item().Text(t2).Bold();
                        col.Item().Height(3);
                        col.Item().Text(text =>
                        {
                            string verb = isSsm ? "efectuată" : "efectuat";
                            text.Span($"a fost {verb} la data ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingDate?.ToString("dd.MM.yyyy"))).Underline().FontSize(10);
                            text.Span(" loc de muncă/post de lucru ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingLocation ?? user.Function?.Name)).Underline().FontSize(10);
                            text.Span(" timp de ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingHours?.ToString())).Underline().FontSize(10);
                            text.Span(" ore, de către ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingInstructor ?? managerName)).Underline().FontSize(10);
                            text.Span(" având funcția de ").FontSize(10);
                            text.Span(FUnderline(user.WorkplaceTrainingInstructorFunction ?? managerFunction)).Underline().FontSize(10);
                        });
                        col.Item().Height(3);
                        col.Item().Text("Conținutul instruirii:").Bold();
                        col.Item().Border(0.5f).Padding(6)
                            .Text(string.IsNullOrWhiteSpace(user.WorkplaceTrainingContent) ? " " : user.WorkplaceTrainingContent).FontSize(10);
                        SignatureRow(col, isSsm);

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

                            string occupation = user.Function?.Name ?? "";
                            for (int i = 0; i < 14; i++)
                            {
                                static IContainer DataCell(IContainer c) =>
                                    c.Border(0.5f).Padding(2).MinHeight(14);

                                table.Cell().Element(DataCell).Text("");
                                table.Cell().Element(DataCell).Text("");
                                table.Cell().Element(DataCell).Text(occupation).FontSize(7);
                                table.Cell().Element(DataCell).Text("");
                                table.Cell().Element(DataCell).Text("");
                                table.Cell().Element(DataCell).Text("");
                                if (isSsm) table.Cell().Element(DataCell).Text("");
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Pag. "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages();
                    });
                });

                // ══════════════════════════════════════════════════════
                // PAGE 4 — SEMNĂTURI DIGITALE
                // ══════════════════════════════════════════════════════
                if (document.UserSignedAt.HasValue || document.ManagerSignedAt.HasValue)
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Column(col =>
                        {
                            col.Item().Background(Colors.Grey.Lighten3).Padding(6)
                                .Text("SEMNĂTURI DIGITALE").Bold().FontSize(12);
                            col.Item().Height(16);

                            // Employee signature block
                            col.Item().Column(c =>
                            {
                                c.Item().Text("Semnătura angajat:").Bold().FontSize(10);
                                c.Item().Height(6);
                                var empSig = TryDecodeSignature(document.UserSignatureData);
                                if (empSig != null)
                                {
                                    c.Item().Width(200).Image(empSig).FitWidth();
                                    c.Item().Height(4);
                                    c.Item().Text($"Semnat: {document.UserSignedAt?.ToString("dd.MM.yyyy HH:mm")} UTC")
                                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                                    if (!string.IsNullOrEmpty(document.UserSignatureIpAddress))
                                        c.Item().Text($"IP: {document.UserSignatureIpAddress}")
                                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    c.Item().PaddingTop(50).BorderBottom(0.5f).Width(200).Text("");
                                    if (document.UserSignedAt.HasValue)
                                        c.Item().Text($"Semnat: {document.UserSignedAt?.ToString("dd.MM.yyyy HH:mm")} UTC")
                                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                            });

                            col.Item().Height(20);

                            // Manager signature block
                            col.Item().Column(c =>
                            {
                                c.Item().Text("Semnătura manager:").Bold().FontSize(10);
                                c.Item().Height(6);
                                var mgrSig = TryDecodeSignature(document.ManagerSignatureData);
                                if (mgrSig != null)
                                {
                                    c.Item().Width(200).Image(mgrSig).FitWidth();
                                    c.Item().Height(4);
                                    c.Item().Text($"Semnat: {document.ManagerSignedAt?.ToString("dd.MM.yyyy HH:mm")} UTC")
                                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                                    if (!string.IsNullOrEmpty(document.ManagerSignatureIpAddress))
                                        c.Item().Text($"IP: {document.ManagerSignatureIpAddress}")
                                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    c.Item().PaddingTop(50).BorderBottom(0.5f).Width(200).Text("");
                                    if (document.ManagerSignedAt.HasValue)
                                        c.Item().Text($"Semnat: {document.ManagerSignedAt?.ToString("dd.MM.yyyy HH:mm")} UTC")
                                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                            });

                            // Cryptographic audit line
                            if (!string.IsNullOrEmpty(document.DocumentHash))
                            {
                                col.Item().Height(20);
                                col.Item().Text($"SHA-256: {document.DocumentHash}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                            }
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
            var doc = await _context.UserDocuments
                .Include(d => d.User)
                    .ThenInclude(u => u.Department)
                .Include(d => d.User)
                    .ThenInclude(u => u.Function)
                .Include(d => d.User)
                    .ThenInclude(u => u.AssignedTo)
                        .ThenInclude(m => m!.Function)
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
                doc.ManagerSignatureMethod = signatureMethod;
                doc.ManagerSignatureData = signatureData;
                doc.ManagerSignatureIpAddress = ipAddress;
                doc.ManagerSignedAt = timestamp;
                doc.ManagerCryptographicSignature = cryptoSignature;
                doc.Status = "Completed";
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
    }
}
