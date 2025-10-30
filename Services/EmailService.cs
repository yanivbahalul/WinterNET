using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HelloWorldWeb.Services
{
    public class EmailService
    {
        // SMTP fields kept for backward compatibility but unused in simplified flow
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly bool _useSsl;
        private readonly string _emailTo;
        private readonly string _emailFrom;
        private readonly string _sendGridKey;

        public bool IsConfigured { get; }

        public EmailService(IConfiguration configuration)
        {
            Console.WriteLine("[EmailService] Initializing EmailService...");
            
            string Get(string primary, string fallback1 = null, string fallback2 = null)
                => Environment.GetEnvironmentVariable(primary)
                   ?? (fallback1 != null ? Environment.GetEnvironmentVariable(fallback1) : null)
                   ?? (fallback2 != null ? Environment.GetEnvironmentVariable(fallback2) : null)
                   ?? configuration[$"{primary.Replace("__", ":").Replace("Email", "Email")}"]
                   ?? string.Empty;

            _smtpHost = Get("Email__SmtpHost", "EmailSmtpHost");
            var portStr = Get("Email__SmtpPort", "EmailSmtpPort");
            _smtpPort = int.TryParse(portStr, out var p) ? p : 587;
            _smtpUser = Get("Email__SmtpUser");
            var pass = Get("Email__SmtpPass", "EMAIL_SMTP_PASS").Replace(" ", "");
            _smtpPass = string.IsNullOrWhiteSpace(pass) ? configuration["Email:SmtpPass"] ?? string.Empty : pass;
            _useSsl = (Get("Email__UseSsl", "EmailUseSsl").ToLowerInvariant() == "true") || true;
            _emailTo = Get("EMAIL_TO");
            _emailFrom = Get("EMAIL_FROM");
            if (string.IsNullOrWhiteSpace(_emailFrom)) _emailFrom = _smtpUser;
            if (string.IsNullOrWhiteSpace(_smtpHost)) _smtpHost = "smtp.gmail.com";

            // Optional SendGrid API key for API-based sending (Render-friendly)
            _sendGridKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");

            // Simplified: consider configured if SendGrid is available with from/to
            var sendGridConfigured = !string.IsNullOrWhiteSpace(_sendGridKey)
                           && !string.IsNullOrWhiteSpace(_emailFrom)
                           && !string.IsNullOrWhiteSpace(_emailTo);

            IsConfigured = sendGridConfigured;

            // DEBUG: Print configuration status
            Console.WriteLine($"[EmailService] Configuration loaded:");
            Console.WriteLine($"  - SmtpHost: {(_smtpHost ?? "NULL")}");
            Console.WriteLine($"  - SmtpPort: {_smtpPort}");
            Console.WriteLine($"  - SmtpUser: {(_smtpUser ?? "NULL")}");
            Console.WriteLine($"  - SmtpPass: {(string.IsNullOrWhiteSpace(_smtpPass) ? "NULL/EMPTY" : "***SET***")}");
            Console.WriteLine($"  - UseSsl: {_useSsl}");
            Console.WriteLine($"  - EmailTo: {(_emailTo ?? "NULL")}");
            Console.WriteLine($"  - EmailFrom: {(_emailFrom ?? "NULL")}");
            Console.WriteLine($"  - IsConfigured: {IsConfigured}");
            Console.WriteLine($"  - SendGrid: {(string.IsNullOrWhiteSpace(_sendGridKey) ? "NOT SET" : "SET")}");
            
            if (!IsConfigured)
            {
                Console.WriteLine("[EmailService] ❌ WARNING: EmailService is NOT properly configured!");
                Console.WriteLine("[EmailService] Missing configuration:");
                if (string.IsNullOrWhiteSpace(_smtpHost)) Console.WriteLine("  - SmtpHost is missing");
                if (string.IsNullOrWhiteSpace(_smtpUser)) Console.WriteLine("  - SmtpUser is missing");
                if (string.IsNullOrWhiteSpace(_smtpPass)) Console.WriteLine("  - SmtpPass is missing");
                if (string.IsNullOrWhiteSpace(_emailTo)) Console.WriteLine("  - EmailTo (EMAIL_TO) is missing");
            }
            else
            {
                Console.WriteLine("[EmailService] ✅ EmailService is properly configured");
            }
        }

        public bool Send(string subject, string htmlBody)
        {
            Console.WriteLine($"[EmailService] Send() called");
            Console.WriteLine($"  - Subject: {subject}");
            Console.WriteLine($"  - Body length: {htmlBody?.Length ?? 0} chars");
            Console.WriteLine($"  - IsConfigured: {IsConfigured}");
            
            if (!IsConfigured)
            {
                Console.WriteLine("[EmailService] ❌ Cannot send - EmailService is NOT configured");
                return false;
            }
            
            try
            {
                // Simplest flow: SendGrid only
                Console.WriteLine("[EmailService] Using SendGrid API (simplified mode)...");
                var ok = SendViaSendGrid(_emailFrom, _emailTo, subject, htmlBody, _sendGridKey);
                Console.WriteLine($"[EmailService] SendGrid result: {ok}");
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] ❌ Send failed!");
                Console.WriteLine($"  - Exception type: {ex.GetType().Name}");
                Console.WriteLine($"  - Message: {ex.Message}");
                Console.WriteLine($"  - StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  - Inner Exception: {ex.InnerException.Message}");
                }
                return false;
            }
        }

        private bool SendViaSendGrid(string fromEmail, string toEmail, string subject, string htmlBody, string apiKey)
        {
            try
            {
                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var payload = new
                {
                    personalizations = new[] {
                        new {
                            to = new[] { new { email = toEmail } }
                        }
                    },
                    from = new { email = fromEmail },
                    subject = subject,
                    content = new[] {
                        new { type = "text/html", value = htmlBody }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = "https://api.sendgrid.com/v3/mail/send";

                Console.WriteLine($"[EmailService] POST {url} (payload length: {json.Length})");
                var resp = http.PostAsync(url, content).GetAwaiter().GetResult();
                var respBody = resp.Content != null ? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() : string.Empty;
                Console.WriteLine($"[EmailService] SendGrid response: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                if (!string.IsNullOrWhiteSpace(respBody))
                {
                    Console.WriteLine($"[EmailService] SendGrid response body: {respBody}");
                }

                // SendGrid returns 202 Accepted on success
                return (int)resp.StatusCode == 202;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] SendViaSendGrid error: {ex.Message}");
                return false;
            }
        }
    }
}


