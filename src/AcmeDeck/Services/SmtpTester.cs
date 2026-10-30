using System.Net;
using System.Net.Mail;

namespace AcmeDeck.Services;

/// <summary>
/// Sends a test message with the given notification settings. win-acme uses
/// MailKit; System.Net.Mail covers plain SMTP and STARTTLS, which is what
/// almost every provider offers on port 25/587. Implicit TLS (465) cannot be
/// tested this way.
/// </summary>
public static class SmtpTester
{
    public static async Task SendAsync(NotificationSettings settings, string? passwordOverride)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Server, sender address and at least one receiver are required.");
        }
        var security = settings.Security;
        if (security == SmtpSecurity.ImplicitTls ||
            (security == SmtpSecurity.Automatic && settings.SmtpPort == 465))
        {
            throw new NotSupportedException(
                "Implicit TLS (port 465) cannot be tested from here. Save the settings and send " +
                "a test message from the win-acme menu instead (More options).");
        }
        var password = passwordOverride ?? settings.SmtpPassword;
        if (password?.StartsWith("vault://", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new NotSupportedException(
                "The password is a reference to the win-acme secret vault and cannot be read here. " +
                "Enter the password to run the test.");
        }

        using var client = new SmtpClient(settings.SmtpServer, settings.SmtpPort)
        {
            EnableSsl = security != SmtpSecurity.None,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000,
        };
        if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUser, password);
        }

        var computer = string.IsNullOrWhiteSpace(settings.ComputerName) ? Environment.MachineName : settings.ComputerName;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.SenderAddress!, settings.SenderName ?? "win-acme"),
            Subject = $"Test notification ({computer})",
            Body = "If you are reading this, notification e-mails from win-acme can be delivered.\r\n\r\n" +
                   $"Sent by AcmeDeck from {computer}.",
        };
        foreach (var receiver in settings.ReceiverAddresses)
        {
            message.To.Add(receiver);
        }
        await client.SendMailAsync(message);
    }
}
