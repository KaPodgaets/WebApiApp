using System.Text.Json;

namespace WebApiApp.PowerBiRemote;

internal static class PowerBiDaxQueryNormalizer
{
    public static string Normalize(string daxQuery)
    {
        if (string.IsNullOrWhiteSpace(daxQuery))
        {
            throw new InvalidOperationException("The DAX query cannot be empty.");
        }

        var trimmed = daxQuery.Trim();
        trimmed = StripCodeFence(trimmed);

        if (trimmed.Length >= 2 &&
            trimmed[0] == '"' &&
            trimmed[^1] == '"')
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<string>(trimmed);
                if (!string.IsNullOrWhiteSpace(deserialized))
                {
                    trimmed = deserialized.Trim();
                }
            }
            catch (JsonException)
            {
            }
        }

        trimmed = trimmed
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);

        return trimmed.Trim();
    }

    private static string StripCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var lines = value.Split('\n');
        if (lines.Length == 0)
        {
            return value;
        }

        var startIndex = 1;
        var endIndex = lines.Length;

        if (lines[^1].Trim().Equals("```", StringComparison.Ordinal))
        {
            endIndex--;
        }

        return string.Join("\n", lines[startIndex..endIndex]).Trim();
    }
}
