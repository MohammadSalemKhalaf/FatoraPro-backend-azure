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
            Credentials = new NetworkCredential(fromEmail, password),

            // Was 15000 - measured live against the production host and it
            // was consistently hitting that exact ceiling (a ~16s round
            // trip, timeout included) rather than failing fast, meaning the
            // Render-to-Gmail SMTP handshake itself is genuinely slower
            // than 15s here, not rejecting the connection outright. Widened
            // to give a slow-but-real relay enough room to complete, while
            // still bounding the original unbounded-multi-minute-hang
            // problem this Timeout was added to fix in the first place.
            // If this is STILL timing out at the new ceiling, that's a
            // different problem this can't fix - it would mean Render is
            // blocking/throttling outbound SMTP (port 587) rather than the
            // relay just being slow, and the real fix is switching to an
            // HTTP-API-based email provider (SendGrid/Resend/Mailgun/
            // Postmark) instead of raw SMTP.
            Timeout = 60000
        };

        using var message = new MailMessage(fromEmail, to, subject, body);
        await client.SendMailAsync(message);
    }
}
