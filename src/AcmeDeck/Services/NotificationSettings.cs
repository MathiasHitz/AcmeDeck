using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;

namespace AcmeDeck.Services;

/// <summary>
/// SmtpSecure / SmtpSecureMode of win-acme, mapped to one choice.
/// Mode values 1-4 correspond to MailKit SecureSocketOptions.
/// </summary>
public enum SmtpSecurity
{
    None = 0,
    Automatic = 1,
    ImplicitTls = 2,
    StartTlsRequired = 3,
    StartTlsWhenAvailable = 4
}

/// <summary>
/// The "Notification" section of win-acme's settings.json. Key names follow
/// the 2.2.9 source code (Settings.cs), not the online documentation, which
/// wrongly lists SmtpSenderName.
/// </summary>
public sealed class NotificationSettings
{
    public string? SmtpServer { get; set; }
    public int SmtpPort { get; set; } = 25;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public SmtpSecurity Security { get; set; } = SmtpSecurity.None;
    public string? SenderName { get; set; }
    public string? SenderAddress { get; set; }
    public List<string> ReceiverAddresses { get; set; } = [];
    public bool EmailOnSuccess { get; set; }
    public string? ComputerName { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpServer) &&
        !string.IsNullOrWhiteSpace(SenderAddress) &&
        ReceiverAddresses.Count > 0;

    public bool PasswordIsVaultReference => SmtpPassword?.StartsWith("vault://", StringComparison.OrdinalIgnoreCase) == true;

    public static NotificationSettings Load(string settingsFile)
    {
        var root = JsonNode.Parse(File.ReadAllText(settingsFile), documentOptions: JsonHelper.ReadOptions);
        var n = root.Get("Notification");
        var secure = n.GetBool("SmtpSecure") ?? false;
        var mode = n.GetInt("SmtpSecureMode") ?? 1;
        return new NotificationSettings
        {
            SmtpServer = n.GetString("SmtpServer"),
            SmtpPort = n.GetInt("SmtpPort") ?? 25,
            SmtpUser = n.GetString("SmtpUser"),
            SmtpPassword = n.GetString("SmtpPassword"),
            Security = !secure ? SmtpSecurity.None : Enum.IsDefined(typeof(SmtpSecurity), mode) ? (SmtpSecurity)mode : SmtpSecurity.Automatic,
            // Fall back to the documented (but ignored) name, so a config that
            // followed the docs is at least shown and then saved correctly.
            SenderName = n.GetString("SenderName") ?? n.GetString("SmtpSenderName"),
            SenderAddress = n.GetString("SenderAddress"),
            ReceiverAddresses = n.GetStringList("ReceiverAddresses"),
            EmailOnSuccess = n.GetBool("EmailOnSuccess") ?? false,
            ComputerName = n.GetString("ComputerName"),
        };
    }

    /// <summary>
    /// Writes only the Notification section; everything else in settings.json
    /// is preserved. A timestamped backup is created first.
    /// </summary>
    public void Save(string settingsFile)
    {
        var root = JsonNode.Parse(File.ReadAllText(settingsFile), documentOptions: JsonHelper.ReadOptions) as JsonObject
                   ?? new JsonObject();
        var key = root.FirstOrDefault(kv => string.Equals(kv.Key, "Notification", StringComparison.OrdinalIgnoreCase)).Key ?? "Notification";
        if (root[key] is not JsonObject n)
        {
            n = new JsonObject();
            root[key] = n;
        }

        n["SmtpServer"] = Null(SmtpServer);
        n["SmtpPort"] = SmtpPort;
        n["SmtpUser"] = Null(SmtpUser);
        n["SmtpPassword"] = Null(SmtpPassword);
        n["SmtpSecure"] = Security != SmtpSecurity.None;
        n["SmtpSecureMode"] = Security == SmtpSecurity.None ? 1 : (int)Security;
        n["SenderName"] = Null(SenderName);
        n.Remove("SmtpSenderName");
        n["SenderAddress"] = Null(SenderAddress);
        n["ReceiverAddresses"] = new JsonArray(ReceiverAddresses.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
        n["EmailOnSuccess"] = EmailOnSuccess;
        n["ComputerName"] = Null(ComputerName);

        File.Copy(settingsFile, settingsFile + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: true);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(settingsFile, root.ToJsonString(options));
    }

    private static string? Null(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
