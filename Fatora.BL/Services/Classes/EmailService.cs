using System.Net;
using System.Net.Mail;
using Fatora.BL.Services.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Fatora.BL.Services.Classes;

// Gmail SMTP specifically, per the product decision to keep this to one fixed provider
// rather than a configurable SmtpSettings surface nothing else currently needs.
public class EmailService(IConfiguration configuration) : IEmailService
{
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;

    public async Task SendAsync(string to, string subject, string body)
    {
        var fromEmail = configuration["AdminRecovery:Email"]!;

        // Deliberately read directly from the environment, not appsettings.json - this is a real
        // Gmail App Password, and committing it to source control would hand repo access to the mailbox.
        var password = configuration["SMTP_PASSWORD"]!;

        using var client = new SmtpClient(SmtpHost, SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(fromEmail, password)
        };

        using var message = new MailMessage(fromEmail, to, subject, body);
        await client.SendMailAsync(message);
    }
}
