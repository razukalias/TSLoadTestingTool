using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Execution;

public static class TemplateRenderer
{
    private const string Name = @"[\p{L}\p{N}._\-\[\]]+";

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

        var rendered = Regex.Replace(value, $@"_(?:\*({Name})\*|({Name}))_", match =>
            variables.TryGetValue(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, out var replacement)
                ? replacement
                : throw new InvalidDataException($"Missing request variable '{(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)}'."));
        return contentType.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? RemoveEmptyJsonObjects(rendered)
            : rendered;
    }

    private static string RemoveJsonProperty(string json, string escapedVariable)
    {
        var valueToken = "(?:\"(?:_" + escapedVariable + "_|_\\*" + escapedVariable + "\\*_)\"|(?:_" + escapedVariable + "_|_\\*" + escapedVariable + "\\*_))";
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

    private static string RemoveEmptyJsonObjects(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null) return json;
            PruneEmptyObjects(root, isRoot: true);
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            // Preserve the original renderer error path for malformed JSON.
            // HTTP execution will report the malformed payload as it did before.
            return json;
        }
    }

    private static bool PruneEmptyObjects(JsonNode node, bool isRoot)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is null) continue;
                if (PruneEmptyObjects(property.Value, isRoot: false)) jsonObject.Remove(property.Key);
            }
            return !isRoot && jsonObject.Count == 0;
        }

        if (node is JsonArray jsonArray)
        {
            for (var index = jsonArray.Count - 1; index >= 0; index--)
            {
                var item = jsonArray[index];
                if (item is not null && PruneEmptyObjects(item, isRoot: false)) jsonArray.RemoveAt(index);
            }
        }
        return false;
    }
}
