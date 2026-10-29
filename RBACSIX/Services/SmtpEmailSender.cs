using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace RBACSIX.Services;

public sealed class SmtpEmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class PasswordResetOptions
{
    public string? PublicBaseUrl { get; set; }
}

public interface IPasswordResetEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
}

public sealed class SmtpEmailSender(
    IOptions<SmtpEmailOptions> options) : IPasswordResetEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Password reset email delivery is not configured.");
        if (!MailAddress.TryCreate(settings.FromAddress, out var fromAddress) ||
            !MailAddress.TryCreate(email, out var toAddress) ||
            settings.Port is < 1 or > 65535)
            throw new InvalidOperationException("Password reset email configuration or recipient is invalid.");

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress.Address, settings.FromName ?? string.Empty),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };
        message.To.Add(toAddress);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password ?? string.Empty);

        await client.SendMailAsync(message);
    }
}
