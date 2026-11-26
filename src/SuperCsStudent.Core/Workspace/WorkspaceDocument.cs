using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperCsStudent.Core.Workspace;

public enum StudioKind
{
    Automata,
    Algorithms,
    Structures
}

public sealed record WorkspaceDocument(
    string Id,
    string Title,
    StudioKind Studio,
    string Subtitle,
    DateTimeOffset UpdatedAt,
    JsonElement Payload);

public static class WorkspaceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
