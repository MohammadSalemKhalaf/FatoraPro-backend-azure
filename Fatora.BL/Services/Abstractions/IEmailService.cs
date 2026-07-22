namespace Fatora.BL.Services.Abstractions;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}
