using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace HelloWorldWeb.Services
{
    public class EmailService
    {
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
            // support both Email__SmtpPass and EMAIL_SMTP_PASS; also strip spaces (Gmail app password)
            var pass = Get("Email__SmtpPass", "EMAIL_SMTP_PASS").Replace(" ", "");
            _smtpPass = string.IsNullOrWhiteSpace(pass) ? configuration["Email:SmtpPass"] ?? string.Empty : pass;
            _useSsl = (Get("Email__UseSsl", "EmailUseSsl").ToLowerInvariant() == "true") || true;
            _emailTo = Get("EMAIL_TO");
            _emailFrom = Get("EMAIL_FROM");
            if (string.IsNullOrWhiteSpace(_emailFrom)) _emailFrom = _smtpUser;
            if (string.IsNullOrWhiteSpace(_smtpHost)) _smtpHost = "smtp.gmail.com";

            IsConfigured = !string.IsNullOrWhiteSpace(_smtpHost)
                           && !string.IsNullOrWhiteSpace(_smtpUser)
                           && !string.IsNullOrWhiteSpace(_smtpPass)
                           && !string.IsNullOrWhiteSpace(_emailTo);
        }

        public bool Send(string subject, string htmlBody)
        {
            if (!IsConfigured) return false;
            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(_emailFrom);
                message.To.Add(_emailTo);
                message.Subject = subject;
                message.Body = htmlBody;
                message.IsBodyHtml = true;

                using var client = new SmtpClient(_smtpHost, _smtpPort)
                {
                    EnableSsl = _useSsl,
                    Credentials = new NetworkCredential(_smtpUser, _smtpPass)
                };

                client.Send(message);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] Send failed: {ex.Message}");
                return false;
            }
        }
    }
}


