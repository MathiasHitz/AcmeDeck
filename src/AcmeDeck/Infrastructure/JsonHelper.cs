using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AcmeDeck.Infrastructure;

/// <summary>
/// Tolerant accessors for the win-acme JSON files. Property names are matched
/// case-insensitively and missing or unexpected values never throw, so a newer
/// win-acme version with extra fields does not break the overview.
/// </summary>
internal static class JsonHelper
{
    public static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static JsonNode? Get(this JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }
        if (obj.TryGetPropertyValue(name, out var exact))
        {
            return exact;
        }
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }
        return null;
    }

    public static string? GetString(this JsonNode? node, string name) => AsString(node.Get(name));

    public static bool? GetBool(this JsonNode? node, string name)
    {
        if (node.Get(name) is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b))
            {
                return b;
            }
            if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    public static int? GetInt(this JsonNode? node, string name)
    {
        if (node.Get(name) is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i))
            {
                return i;
            }
            if (v.TryGetValue<string>(out var s) &&
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    /// <summary>
    /// win-acme writes UTC timestamps. Returned values are always local time.
    /// </summary>
    public static DateTime? GetDate(this JsonNode? node, string name)
    {
        var s = node.GetString(name);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) && utc.Year > 1)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        }
        return null;
    }

    public static List<string> GetStringList(this JsonNode? node, string name) =>
        node.Get(name) is JsonArray array
            ? array.Select(AsString).OfType<string>().ToList()
            : [];

    public static List<long> GetLongList(this JsonNode? node, string name)
    {
        var result = new List<long>();
        if (node.Get(name) is JsonArray array)
        {
            foreach (var item in array)
            {
                if (long.TryParse(AsString(item), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    result.Add(l);
                }
            }
        }
        return result;
    }

    public static IEnumerable<JsonNode> GetArray(this JsonNode? node, string name) =>
        node.Get(name) is JsonArray array ? array.OfType<JsonNode>() : [];

    private static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue v)
        {
            return null;
        }
        if (v.TryGetValue<string>(out var s))
        {
            return s;
        }
        return v.ToJsonString().Trim('"');
    }
}
