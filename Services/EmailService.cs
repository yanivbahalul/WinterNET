using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace HelloWorldWeb.Services
{
    public class EmailService
    {
        // SMTP fields for Gmail sending
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly bool _useSsl;
        private readonly string _emailTo;
        private readonly string _emailFrom;

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

            // Gmail-only configuration
            var smtpConfigured = !string.IsNullOrWhiteSpace(_smtpHost)
                           && !string.IsNullOrWhiteSpace(_smtpUser)
                           && !string.IsNullOrWhiteSpace(_smtpPass)
                           && !string.IsNullOrWhiteSpace(_emailTo)
                           && !string.IsNullOrWhiteSpace(_emailFrom);

            IsConfigured = smtpConfigured;

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
            Console.WriteLine($"  - SendGrid: DISABLED");
            
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
                Console.WriteLine($"[EmailService] Creating mail message (Gmail SMTP)...");
                Console.WriteLine($"  - From: {_emailFrom}");
                Console.WriteLine($"  - To: {_emailTo}");

                using var message = new MailMessage();
                message.From = new MailAddress(_emailFrom);
                message.To.Add(_emailTo);
                message.Subject = subject;
                message.Body = htmlBody;
                message.IsBodyHtml = true;

                Console.WriteLine($"[EmailService] Connecting to SMTP server...");
                Console.WriteLine($"  - Host: {_smtpHost}");
                Console.WriteLine($"  - Port: {_smtpPort}");
                Console.WriteLine($"  - SSL: {_useSsl}");
                Console.WriteLine($"  - User: {_smtpUser}");

                using var client = new SmtpClient(_smtpHost, _smtpPort)
                {
                    EnableSsl = _useSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_smtpUser, _smtpPass),
                    Timeout = 15000
                };

                Console.WriteLine($"[EmailService] SMTP client configured (Timeout: {client.Timeout}ms). Sending email...");
                client.Send(message);
                Console.WriteLine($"[EmailService] ✅ Email sent successfully via Gmail SMTP!");
                return true;
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
    }
}


