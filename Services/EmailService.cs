using System;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HelloWorldWeb.Services
{
    public class EmailService
    {
        private readonly string _emailTo;
        private readonly string _emailFrom;
        private readonly string _sendGridKey;

        public bool IsConfigured { get; }

        public EmailService(IConfiguration configuration)
        {
            Console.WriteLine("[EmailService] Initializing EmailService (SendGrid-only mode)...");
            
            _emailTo = Environment.GetEnvironmentVariable("EMAIL_TO") ?? configuration["Email:To"];
            _emailFrom = Environment.GetEnvironmentVariable("EMAIL_FROM") ?? configuration["Email:From"];
            _sendGridKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");

            IsConfigured = !string.IsNullOrWhiteSpace(_sendGridKey)
                           && !string.IsNullOrWhiteSpace(_emailFrom)
                           && !string.IsNullOrWhiteSpace(_emailTo);

            // DEBUG: Print configuration status
            Console.WriteLine($"[EmailService] Configuration loaded:");
            Console.WriteLine($"  - EmailTo: {(_emailTo ?? "NULL")}");
            Console.WriteLine($"  - EmailFrom: {(_emailFrom ?? "NULL")}");
            Console.WriteLine($"  - SendGridKey: {(string.IsNullOrWhiteSpace(_sendGridKey) ? "NOT SET" : "***SET***")}");
            Console.WriteLine($"  - IsConfigured: {IsConfigured}");
            
            if (!IsConfigured)
            {
                Console.WriteLine("[EmailService] ❌ WARNING: EmailService is NOT properly configured!");
                Console.WriteLine("[EmailService] Missing configuration:");
                if (string.IsNullOrWhiteSpace(_sendGridKey)) Console.WriteLine("  - SENDGRID_API_KEY is missing");
                if (string.IsNullOrWhiteSpace(_emailFrom)) Console.WriteLine("  - EMAIL_FROM is missing");
                if (string.IsNullOrWhiteSpace(_emailTo)) Console.WriteLine("  - EMAIL_TO is missing");
            }
            else
            {
                Console.WriteLine("[EmailService] ✅ EmailService is properly configured (SendGrid)");
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
                Console.WriteLine("[EmailService] Sending via SendGrid API...");
                var ok = SendViaSendGrid(_emailFrom, _emailTo, subject, htmlBody, _sendGridKey);
                if (ok)
                {
                    Console.WriteLine("[EmailService] ✅ Email sent successfully via SendGrid!");
                }
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

                Console.WriteLine($"[EmailService] POST {url}");
                var resp = http.PostAsync(url, content).GetAwaiter().GetResult();
                var respBody = resp.Content != null ? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() : string.Empty;
                Console.WriteLine($"[EmailService] SendGrid response: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                
                if ((int)resp.StatusCode == 202)
                {
                    return true;
                }
                
                if (!string.IsNullOrWhiteSpace(respBody))
                {
                    Console.WriteLine($"[EmailService] SendGrid error body: {respBody}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] SendGrid error: {ex.Message}");
                return false;
            }
        }
    }
}


