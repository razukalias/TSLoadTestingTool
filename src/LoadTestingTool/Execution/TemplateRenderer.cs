using System.Text.RegularExpressions;

namespace LoadTestingTool.Execution;

public static class TemplateRenderer
{
    private const string Name = @"[\p{L}\p{N}._-]+";

    public static string Render(string raw, string contentType, IReadOnlyDictionary<string, string> variables)
    {
        var value = raw ?? string.Empty;
        foreach (var pair in variables.Where(x => string.IsNullOrEmpty(x.Value)))
        {
            var escaped = Regex.Escape(pair.Key);
            if (contentType.Equals("json", StringComparison.OrdinalIgnoreCase))
                value = RemoveJsonProperty(value, escaped);
            else if (contentType.Equals("xml", StringComparison.OrdinalIgnoreCase))
                value = Regex.Replace(value, $@"\s*<(?<tag>[\w:.-]+)>\s*_{escaped}_\s*</\k<tag>>", string.Empty, RegexOptions.IgnoreCase);
        }

        return Regex.Replace(value, $@"_({Name})_", match =>
            variables.TryGetValue(match.Groups[1].Value, out var replacement)
                ? replacement
                : throw new InvalidDataException($"Missing request variable '{match.Groups[1].Value}'."));
    }

    private static string RemoveJsonProperty(string json, string escapedVariable)
    {
        var valueToken = "(?:\"_" + escapedVariable + "_\"|_" + escapedVariable + "_)";
        // Prefer removing the property together with its following comma. This handles
        // first and middle properties without leaving a trailing comma.
        var withFollowingComma = "\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + ",\\s*";
        json = Regex.Replace(json, withFollowingComma, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // If it was the final property, remove its preceding comma instead.
        var finalProperty = ",\\s*\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + "(?=\\s*[}])";
        json = Regex.Replace(json, finalProperty, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Handle a single-property object or a final property that has no preceding comma.
        var onlyProperty = "\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + "(?=\\s*[}])";
        json = Regex.Replace(json, onlyProperty, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return json;
    }
}
