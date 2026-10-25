using System.Text.Json;

namespace IslandWar.Sim;

/// <summary>How the tunables in <c>data/*.json</c> are read: snake_case names, comments and trailing commas allowed.</summary>
public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Parse<T>(string json, string fileName)
    {
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidDataException($"{fileName} was empty.");
    }
}
