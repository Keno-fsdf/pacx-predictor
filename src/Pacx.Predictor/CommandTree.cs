using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pacx.Predictor;

/// <summary>The command tree as printed by <c>pacx completion export</c>.</summary>
public sealed class CommandTree
{
    [JsonPropertyName("commands")]
    public List<CommandNode> Commands { get; set; } = new();

    [JsonPropertyName("namespaces")]
    public List<NamespaceNode> Namespaces { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public static CommandTree? Parse(string? raw) => Parse(raw, out _);

    /// <summary>
    /// Parses the raw stdout of <c>pacx completion export</c>. Only the text between the
    /// first '{' and the last '}' is used, so banner or warning lines around it do no harm.
    /// <paramref name="json"/> receives the cleaned JSON text.
    /// </summary>
    public static CommandTree? Parse(string? raw, out string? json)
    {
        json = null;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        json = StripControlCharacters(raw.AsSpan(start, end - start + 1));
        try
        {
            var tree = JsonSerializer.Deserialize<CommandTree>(json, JsonOptions);
            return tree is null ? null : Normalize(tree);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Replaces explicit JSON nulls with empty lists so callers never need null checks.</summary>
    private static CommandTree Normalize(CommandTree tree)
    {
        tree.Commands ??= new();
        tree.Namespaces ??= new();
        foreach (var c in tree.Commands)
        {
            c.Verbs ??= new();
            c.Aliases ??= new();
            c.Options ??= new();
        }
        foreach (var n in tree.Namespaces)
        {
            n.Verbs ??= new();
        }
        return tree;
    }

    /// <summary>
    /// With redirected stdout the console code page can turn non-ASCII characters in help
    /// texts into control characters (0x1A), which are not allowed inside JSON strings.
    /// </summary>
    private static string StripControlCharacters(ReadOnlySpan<char> text)
    {
        var buffer = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            buffer[i] = c < ' ' && c is not ('\r' or '\n' or '\t') ? ' ' : c;
        }
        return new string(buffer);
    }
}

public sealed class CommandNode
{
    [JsonPropertyName("verbs")]
    public List<string> Verbs { get; set; } = new();

    /// <summary>Alternative verb chains for the same command.</summary>
    [JsonPropertyName("aliases")]
    public List<List<string>> Aliases { get; set; } = new();

    [JsonPropertyName("help")]
    public string? Help { get; set; }

    [JsonPropertyName("options")]
    public List<OptionNode> Options { get; set; } = new();

    /// <summary>The verbs plus every alias chain.</summary>
    public IEnumerable<IReadOnlyList<string>> VerbChains()
    {
        yield return Verbs;
        foreach (var alias in Aliases)
        {
            if (alias.Count > 0) yield return alias;
        }
    }
}

public sealed class OptionNode
{
    [JsonPropertyName("long")]
    public string Long { get; set; } = string.Empty;

    [JsonPropertyName("short")]
    public string? Short { get; set; }

    [JsonPropertyName("help")]
    public string? Help { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }

    /// <summary>Fixed set of accepted values, or null for free text.</summary>
    [JsonPropertyName("values")]
    public List<string>? Values { get; set; }

    public string LongToken => "--" + Long;

    public bool MatchesToken(string token) =>
        token.Equals(LongToken, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(Short) && token.Equals("-" + Short, StringComparison.OrdinalIgnoreCase));
}

public sealed class NamespaceNode
{
    [JsonPropertyName("verbs")]
    public List<string> Verbs { get; set; } = new();

    [JsonPropertyName("help")]
    public string? Help { get; set; }
}
