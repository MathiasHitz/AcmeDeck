using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AcmeDeck.Services;

public sealed record WacsRunResult(int ExitCode, bool Cancelled, IReadOnlyList<string> Output);

/// <summary>
/// Runs wacs.exe unattended and streams its console output line by line.
/// Standard input is closed right away so wacs can never hang on a prompt.
/// </summary>
public static partial class WacsRunner
{
    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape();

    public static async Task<WacsRunResult> RunAsync(
        string wacsPath,
        IReadOnlyList<string> arguments,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(wacsPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(wacsPath)!,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var lines = new List<string>();
        void Handle(string? data)
        {
            if (data == null)
            {
                return;
            }
            var clean = AnsiEscape().Replace(data, "").TrimEnd();
            lock (lines)
            {
                lines.Add(clean);
            }
            onLine(clean);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        onLine("> " + FormatCommandLine(wacsPath, arguments));
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
            await process.WaitForExitAsync(CancellationToken.None);
        }
        // Drain the asynchronous readers
        process.WaitForExit();
        return new WacsRunResult(cancelled ? -1 : process.ExitCode, cancelled, lines);
    }

    public static string FormatCommandLine(string wacsPath, IEnumerable<string> arguments) =>
        Path.GetFileName(wacsPath) + " " + string.Join(" ", arguments.Select(Quote));

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Any(c => char.IsWhiteSpace(c) || c == '"')
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
}
