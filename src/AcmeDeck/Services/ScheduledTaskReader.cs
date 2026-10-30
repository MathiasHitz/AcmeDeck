using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;

namespace AcmeDeck.Services;

public sealed class ScheduledTaskInfo
{
    public required string Name { get; init; }
    public string TaskPath { get; init; } = "\\";
    public string State { get; init; } = "";
    public bool Enabled { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? RunAs { get; init; }
    public DateTime? LastRun { get; init; }
    public int? LastResult { get; init; }
    public DateTime? NextRun { get; init; }

    public string FullName => TaskPath.TrimEnd('\\') + "\\" + Name;

    /// <summary>0x41303 = SCHED_S_TASK_HAS_NOT_RUN</summary>
    public bool HasRun => LastResult is not null and not 0x41303 && LastRun is { Year: > 2000 };

    public bool LastRunFailed => HasRun && LastResult != 0;

    public string LastResultText => !HasRun ? "Not run yet" :
        LastResult == 0 ? "Success (0x0)" : $"Error (0x{LastResult:X})";
}

/// <summary>
/// Queries the Task Scheduler through the ScheduledTasks PowerShell module.
/// Unlike schtasks.exe its output is not localized, which matters on German servers.
/// </summary>
public static class ScheduledTaskReader
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $tasks = Get-ScheduledTask | Where-Object {
            $_.TaskName -like '* renew (*)' -or ($_.Actions | Where-Object { $_.Execute -like '*wacs.exe*' })
        }
        $result = foreach ($t in $tasks) {
            $i = $t | Get-ScheduledTaskInfo
            $a = $t.Actions | Select-Object -First 1
            [pscustomobject]@{
                Name = $t.TaskName
                TaskPath = $t.TaskPath
                State = [string]$t.State
                Enabled = [bool]$t.Settings.Enabled
                Execute = $a.Execute
                Arguments = $a.Arguments
                WorkingDirectory = $a.WorkingDirectory
                RunAs = $t.Principal.UserId
                LastRun = if ($i.LastRunTime) { $i.LastRunTime.ToString('o') } else { $null }
                LastResult = $i.LastTaskResult
                NextRun = if ($i.NextRunTime) { $i.NextRunTime.ToString('o') } else { $null }
            }
        }
        ConvertTo-Json -InputObject @($result) -Depth 3 -Compress
        """;

    public static async Task<List<ScheduledTaskInfo>> FindWinAcmeTasksAsync()
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        // Force UTF-8 output so task names with umlauts survive
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + Script)));

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("Reading the scheduled tasks took too long.");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Reading the scheduled tasks failed: " + error.Trim());
        }

        var result = new List<ScheduledTaskInfo>();
        var json = JsonNode.Parse(string.IsNullOrWhiteSpace(output) ? "[]" : output.Trim());
        var items = json is JsonArray array ? array.OfType<JsonNode>() : json == null ? [] : [json];
        foreach (var item in items)
        {
            result.Add(new ScheduledTaskInfo
            {
                Name = item.GetString("Name") ?? "?",
                TaskPath = item.GetString("TaskPath") ?? "\\",
                State = item.GetString("State") ?? "",
                Enabled = item.GetBool("Enabled") ?? false,
                ExecutablePath = item.GetString("Execute")?.Trim('"'),
                Arguments = item.GetString("Arguments"),
                WorkingDirectory = item.GetString("WorkingDirectory"),
                RunAs = item.GetString("RunAs"),
                LastRun = ParseLocal(item.GetString("LastRun")),
                LastResult = item.GetInt("LastResult"),
                NextRun = ParseLocal(item.GetString("NextRun")),
            });
        }
        return result;
    }

    private static DateTime? ParseLocal(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) && d.Year > 2000
            ? d.ToLocalTime()
            : null;
}
