using System.Text.Json.Nodes;
using AcmeDeck.Infrastructure;
using AcmeDeck.Models;

namespace AcmeDeck.Services;

/// <summary>
/// Reads the renewal definitions straight from disk. All changes go through
/// wacs.exe, this class never writes.
/// </summary>
public static class RenewalStore
{
    public sealed record LoadResult(List<Renewal> Renewals, List<string> Errors);

    public static LoadResult Load(string configurationPath)
    {
        var renewals = new List<Renewal>();
        var errors = new List<string>();
        if (!Directory.Exists(configurationPath))
        {
            return new LoadResult(renewals, errors);
        }
        foreach (var file in Directory.EnumerateFiles(configurationPath, "*.renewal.json"))
        {
            try
            {
                var text = ReadShared(file);
                var root = JsonNode.Parse(text, documentOptions: JsonHelper.ReadOptions);
                if (root != null)
                {
                    renewals.Add(Renewal.Parse(root, file));
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return new LoadResult(renewals.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(), errors);
    }

    public static Renewal? Find(string configurationPath, string id)
    {
        var file = Path.Combine(configurationPath, $"{id}.renewal.json");
        if (!File.Exists(file))
        {
            return null;
        }
        var root = JsonNode.Parse(ReadShared(file), documentOptions: JsonHelper.ReadOptions);
        return root == null ? null : Renewal.Parse(root, file);
    }

    /// <summary>
    /// wacs.exe may be writing the file while we read it.
    /// </summary>
    public static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
