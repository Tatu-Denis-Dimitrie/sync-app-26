using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly IStringLocalizer _localizer;

        public SmtpEmailService(IConfiguration configuration, ILocalizationService localizationService)
        {
            _configuration = configuration;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Emails);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _configuration["Smtp:Host"];
            var port = _configuration.GetValue<int?>("Smtp:Port") ?? 587;
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"];
            var fromName = _configuration["Smtp:FromName"] ?? "SyncApp26";
            var enableSsl = _configuration.GetValue<bool?>("Smtp:EnableSsl") ?? true;

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(fromEmail))
            {
                throw new InvalidOperationException("SMTP settings are missing. Please configure Smtp:Host, Smtp:Username, Smtp:Password and Smtp:FromEmail.");
            }

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            message.To.Add(toEmail);

            // SmtpClient.SendMailAsync uses the legacy APM callback pipeline (see the
            // SendMailAsyncResult frames in any exception from this call) — on failure it can throw
            // on the I/O completion thread in a way that bypasses normal Task exception propagation
            // and crashes the whole process instead of surfacing through this method's try/catch.
            // The synchronous Send(), run on a pool thread, propagates exceptions the normal way.
            await Task.Run(() => client.Send(message));
        }

        public async Task SendVerificationEmailAsync(string toEmail, string firstName, string verifyUrl)
        {
            var subject = _localizer["verifyAccount.subject"].Value;
            var htmlBody = BuildVerificationEmailTemplate(firstName, verifyUrl);
            await SendEmailAsync(toEmail, subject, htmlBody);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string firstName, string resetUrl, int expirationMinutes)
        {
            var subject = _localizer["resetPassword.subject"].Value;
            var htmlBody = BuildPasswordResetEmailTemplate(firstName, resetUrl, expirationMinutes);
            await SendEmailAsync(toEmail, subject, htmlBody);
        }

        private string BuildVerificationEmailTemplate(string firstName, string verifyUrl)
        {
            var encodedFirstName = WebUtility.HtmlEncode(firstName);
            var encodedVerifyUrl = WebUtility.HtmlEncode(verifyUrl);

            var html = new StringBuilder();
            html.Append("<!doctype html>");
            html.Append("<html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append($"<title>{_localizer["verify.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'>");
            html.Append("<tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#09637E 0%,#088395 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#dbeafe;font-size:14px;'>{_localizer["verify.headerTag"].Value}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNamed", $"<strong>{encodedFirstName}</strong>"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["verify.intro"].Value}</p>");
            html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 18px;'><tr><td>");
            html.Append($"<a href='{encodedVerifyUrl}' style='display:inline-block;background:#088395;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-size:14px;font-weight:600;'>{_localizer["verify.button"].Value}</a>");
            html.Append("</td></tr></table>");
            html.Append($"<p style='margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["verify.expiry"].Value}</p>");
            html.Append($"<p style='margin:0;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["common.buttonFallbackIntro"].Value}</p>");
            html.Append($"<p style='margin:8px 0 0;word-break:break-all;font-size:12px;line-height:1.6;color:#0f766e;'>{encodedVerifyUrl}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerUserManagement"].Value}</p>");
            html.Append("</td></tr></table>");
            html.Append("</td></tr></table></body></html>");

            return html.ToString();
        }

        private string BuildPasswordResetEmailTemplate(string firstName, string resetUrl, int expirationMinutes)
        {
            var encodedFirstName = WebUtility.HtmlEncode(firstName);
            var encodedResetUrl = WebUtility.HtmlEncode(resetUrl);

            var html = new StringBuilder();
            html.Append("<!doctype html>");
            html.Append("<html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append($"<title>{_localizer["reset.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'>");
            html.Append("<tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#09637E 0%,#088395 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#dbeafe;font-size:14px;'>{_localizer["reset.headerTag"].Value}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNamed", $"<strong>{encodedFirstName}</strong>"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["reset.intro"].Value}</p>");
            html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 18px;'><tr><td>");
            html.Append($"<a href='{encodedResetUrl}' style='display:inline-block;background:#088395;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-size:14px;font-weight:600;'>{_localizer["reset.button"].Value}</a>");
            html.Append("</td></tr></table>");
            html.Append($"<p style='margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["reset.expiry", expirationMinutes].Value}</p>");
            html.Append($"<p style='margin:0;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["common.buttonFallbackIntro"].Value}</p>");
            html.Append($"<p style='margin:8px 0 0;word-break:break-all;font-size:12px;line-height:1.6;color:#0f766e;'>{encodedResetUrl}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerUserManagement"].Value}</p>");
            html.Append("</td></tr></table>");
            html.Append("</td></tr></table></body></html>");

            return html.ToString();
        }

        public async Task SendDocumentSignatureEmailWithLinkAsync(string toEmail, string documentName, string secureLink)
        {
            var subject = _localizer["signDocument.subject", documentName].Value;
            var encodedDocumentName = WebUtility.HtmlEncode(documentName);
            var encodedLink = WebUtility.HtmlEncode(secureLink);

            var html = new StringBuilder();
            html.Append("<!doctype html>");
            html.Append("<html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append($"<title>{_localizer["docSign.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'>");
            html.Append("<tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#09637E 0%,#088395 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#dbeafe;font-size:14px;'>{_localizer["docSign.headerTag"].Value}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNoName"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["docSign.newDocument", encodedDocumentName].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["docSign.noAccountInstructions"].Value}</p>");
            html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 18px;'><tr><td>");
            html.Append($"<a href='{encodedLink}' style='display:inline-block;background:#088395;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-size:14px;font-weight:600;'>{_localizer["docSign.buttonReviewAndSign"].Value}</a>");
            html.Append("</td></tr></table>");
            html.Append($"<p style='margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["docSign.secureOneTimeLink"].Value}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerSsmSu"].Value}</p>");
            html.Append("</td></tr></table>");
            html.Append("</td></tr></table></body></html>");

            await SendEmailAsync(toEmail, subject, html.ToString());
        }

        public async Task SendDocumentSignatureEmailForRegisteredUserAsync(string toEmail, string documentName, string loginLink)
        {
            var subject = _localizer["signDocument.subject", documentName].Value;
            var encodedDocumentName = WebUtility.HtmlEncode(documentName);
            var encodedLink = WebUtility.HtmlEncode(loginLink);

            var html = new StringBuilder();
            html.Append("<!doctype html>");
            html.Append("<html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append($"<title>{_localizer["docSign.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'>");
            html.Append("<tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#09637E 0%,#088395 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#dbeafe;font-size:14px;'>{_localizer["docSign.headerTag"].Value}</p>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNoName"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["docSign.newDocument", encodedDocumentName].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["docSign.loginInstructions"].Value}</p>");
            html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 18px;'><tr><td>");
            html.Append($"<a href='{encodedLink}' style='display:inline-block;background:#088395;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-size:14px;font-weight:600;'>{_localizer["docSign.buttonLogIn"].Value}</a>");
            html.Append("</td></tr></table>");
            html.Append("</td></tr>");
            html.Append("<tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerSsmSu"].Value}</p>");
            html.Append("</td></tr></table>");
            html.Append("</td></tr></table></body></html>");

            await SendEmailAsync(toEmail, subject, html.ToString());
        }

        public async Task SendMissingSignatureToUserEmailAsync(string toEmail, string firstName, string documentName, DateTime? trainingDate, string? signLink = null)
        {
            var dateString = trainingDate.HasValue ? trainingDate.Value.ToString("dd/MM/yyyy") : _localizer["missingSigUser.dateRecently"].Value;
            var subject = _localizer["missingSignature.subject", documentName].Value;

            var encodedFirstName = WebUtility.HtmlEncode(firstName);
            var encodedDocumentName = WebUtility.HtmlEncode(documentName);
            var encodedDate = WebUtility.HtmlEncode(dateString);

            var html = new StringBuilder();
            html.Append($"<!doctype html><html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>{_localizer["missingSigUser.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'><tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#e11d48 0%,#be123c 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#ffe4e6;font-size:14px;'>{_localizer["missingSigUser.headerTag"].Value}</p>");
            html.Append("</td></tr><tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNamed", $"<strong>{encodedFirstName}</strong>"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["missingSigUser.body", encodedDocumentName, encodedDate].Value}</p>");

            if (!string.IsNullOrEmpty(signLink))
            {
                var encodedLink = WebUtility.HtmlEncode(signLink);
                html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["missingSigUser.noAccountInstructions"].Value}</p>");
                html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 18px;'><tr><td>");
                html.Append($"<a href='{encodedLink}' style='display:inline-block;background:#088395;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;font-size:14px;font-weight:600;'>{_localizer["docSign.buttonReviewAndSign"].Value}</a>");
                html.Append("</td></tr></table>");
                html.Append($"<p style='margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["missingSigUser.personalLink"].Value}</p>");
            }
            else
            {
                html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["missingSigUser.loginInstructions"].Value}</p>");
            }

            html.Append("</td></tr><tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerSsmSu"].Value}</p>");
            html.Append("</td></tr></table></td></tr></table></body></html>");

            await SendEmailAsync(toEmail, subject, html.ToString());
        }

        public async Task SendMissingSignatureToManagerEmailAsync(string toEmail, string managerName, string documentName, int unsignedCount)
        {
            var subject = _localizer["usersMissingSignatures.subject", documentName].Value;

            var encodedManagerName = WebUtility.HtmlEncode(managerName);
            var encodedDocumentName = WebUtility.HtmlEncode(documentName);

            var html = new StringBuilder();
            html.Append($"<!doctype html><html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>{_localizer["missingSigManager.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'><tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:28px 28px 18px;background:linear-gradient(135deg,#ea580c 0%,#c2410c 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:24px;font-weight:700;'>SyncApp26</h1>");
            html.Append($"<p style='margin:8px 0 0;color:#ffedd5;font-size:14px;'>{_localizer["missingSigManager.headerTag"].Value}</p>");
            html.Append("</td></tr><tr><td style='padding:26px 28px;'>");
            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNamed", $"<strong>{encodedManagerName}</strong>"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["missingSigManager.body", unsignedCount, encodedDocumentName].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["missingSigManager.callToAction"].Value}</p>");
            html.Append("</td></tr><tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerSsmSu"].Value}</p>");
            html.Append("</td></tr></table></td></tr></table></body></html>");

            await SendEmailAsync(toEmail, subject, html.ToString());
        }

        public async Task SendSignatureAnomalyAlertEmailAsync(string toEmail, string adminName, int recordsChecked, int anomaliesFound, IReadOnlyList<SweepAnomaly> anomalies)
        {
            var subject = anomaliesFound == 1
                ? _localizer["securityAlert.subjectOne"].Value
                : _localizer["securityAlert.subjectMany", anomaliesFound].Value;

            var encodedAdminName = WebUtility.HtmlEncode(adminName);

            var html = new StringBuilder();
            html.Append($"<!doctype html><html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'><title>{_localizer["securityAlert.title"].Value}</title></head>");
            html.Append("<body style='margin:0;padding:0;background:#f3f4f6;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f3f4f6;padding:24px 12px;'><tr><td align='center'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:620px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e5e7eb;'>");
            html.Append("<tr><td style='padding:20px 28px;background:linear-gradient(135deg,#09637E 0%,#088395 100%);'>");
            html.Append("<h1 style='margin:0;color:#ffffff;font-size:18px;font-weight:700;letter-spacing:-.01em;'>SyncApp26</h1>");
            html.Append("</td></tr><tr><td style='padding:28px 28px 26px;'>");

            // Icon + headline lockup carries the "this is a security alert" signal on its own,
            // so the header above can stay calm brand color instead of a wall of red.
            html.Append("<table role='presentation' cellspacing='0' cellpadding='0' style='margin:0 0 20px;'><tr>");
            html.Append("<td style='padding-right:14px;' valign='middle'>");
            html.Append("<div style='width:44px;height:44px;border-radius:9999px;background:#fee2e2;text-align:center;line-height:44px;'><span style='font-size:20px;font-weight:800;color:#dc2626;'>!</span></div>");
            html.Append("</td><td valign='middle'>");
            html.Append($"<p style='margin:0;font-size:17px;font-weight:700;color:#111827;line-height:1.3;'>{_localizer["securityAlert.headline"].Value}</p>");
            html.Append($"<p style='margin:2px 0 0;font-size:12px;color:#6b7280;'>{_localizer["securityAlert.subhead"].Value}</p>");
            html.Append("</td></tr></table>");

            html.Append($"<p style='margin:0 0 12px;font-size:16px;line-height:1.5;'>{_localizer["common.greetingNamed", $"<strong>{encodedAdminName}</strong>"].Value}</p>");
            html.Append($"<p style='margin:0 0 18px;font-size:15px;line-height:1.6;color:#374151;'>{_localizer["securityAlert.intro"].Value}</p>");

            // Two stat cards side by side read faster than a sentence full of numbers.
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='margin:0 0 20px;'><tr>");
            html.Append("<td width='50%' style='padding-right:6px;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;'><tr><td align='center' style='padding:14px 8px;'>");
            html.Append($"<div style='font-size:28px;font-weight:700;color:#111827;line-height:1.2;'>{recordsChecked}</div>");
            html.Append($"<div style='font-size:11px;font-weight:600;color:#6b7280;text-transform:uppercase;letter-spacing:.05em;margin-top:2px;'>{_localizer["securityAlert.statChecked"].Value}</div>");
            html.Append("</td></tr></table></td>");
            html.Append("<td width='50%' style='padding-left:6px;'>");
            html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#fef2f2;border:1px solid #fecaca;border-radius:10px;'><tr><td align='center' style='padding:14px 8px;'>");
            html.Append($"<div style='font-size:28px;font-weight:700;color:#be123c;line-height:1.2;'>{anomaliesFound}</div>");
            html.Append($"<div style='font-size:11px;font-weight:600;color:#be123c;text-transform:uppercase;letter-spacing:.05em;margin-top:2px;'>{_localizer["securityAlert.statFlagged"].Value}</div>");
            html.Append("</td></tr></table></td>");
            html.Append("</tr></table>");

            if (anomalies.Count > 0)
            {
                html.Append("<table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='margin:0 0 18px;border-collapse:collapse;font-size:13px;'>");
                html.Append($"<tr style='background:#f9fafb;'><th align='left' style='padding:8px 6px;border-bottom:1px solid #e5e7eb;'>{_localizer["securityAlert.colSignatureId"].Value}</th><th align='left' style='padding:8px 6px;border-bottom:1px solid #e5e7eb;'>{_localizer["securityAlert.colSignerUserId"].Value}</th><th align='left' style='padding:8px 6px;border-bottom:1px solid #e5e7eb;'>{_localizer["securityAlert.colStatus"].Value}</th></tr>");
                for (var i = 0; i < anomalies.Count; i++)
                {
                    var anomaly = anomalies[i];
                    var rowBackground = i % 2 == 0 ? "#ffffff" : "#f9fafb";
                    var (pillBackground, pillColor) = anomaly.Status switch
                    {
                        "Invalid" => ("#fee2e2", "#b91c1c"),
                        "ChainBroken" => ("#ffedd5", "#c2410c"),
                        _ => ("#e5e7eb", "#374151")
                    };

                    html.Append($"<tr style='background:{rowBackground};'>");
                    html.Append($"<td style='padding:8px 6px;border-bottom:1px solid #f3f4f6;color:#374151;'>{WebUtility.HtmlEncode(anomaly.SignatureId.ToString())}</td>");
                    html.Append($"<td style='padding:8px 6px;border-bottom:1px solid #f3f4f6;color:#374151;'>{WebUtility.HtmlEncode(anomaly.SignerUserId.ToString())}</td>");
                    html.Append($"<td style='padding:8px 6px;border-bottom:1px solid #f3f4f6;'><span style='display:inline-block;padding:2px 9px;border-radius:9999px;font-size:11px;font-weight:600;background:{pillBackground};color:{pillColor};'>{WebUtility.HtmlEncode(anomaly.Status)}</span></td>");
                    html.Append("</tr>");
                }
                html.Append("</table>");

                if (anomaliesFound > anomalies.Count)
                {
                    html.Append($"<p style='margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["securityAlert.showingFirst", anomalies.Count, anomaliesFound].Value}</p>");
                }
            }

            html.Append($"<p style='margin:0;font-size:13px;line-height:1.6;color:#6b7280;'>{_localizer["securityAlert.footerNote"].Value}</p>");
            html.Append("</td></tr><tr><td style='padding:14px 28px 24px;border-top:1px solid #e5e7eb;'>");
            html.Append($"<p style='margin:0;font-size:12px;color:#9ca3af;'>{_localizer["common.footerSsmSu"].Value}</p>");
            html.Append("</td></tr></table></td></tr></table></body></html>");

            await SendEmailAsync(toEmail, subject, html.ToString());
        }
    }
}
