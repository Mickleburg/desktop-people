using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPeople.Core;

public sealed record AvatarManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("source_type")]
    public required string SourceType { get; init; }

    [JsonPropertyName("body_completion_used")]
    public bool BodyCompletionUsed { get; init; }

    [JsonPropertyName("height_px")]
    public int HeightPx { get; init; }

    [JsonPropertyName("rig")]
    public required string Rig { get; init; }

    [JsonPropertyName("default_behavior")]
    public string DefaultBehavior { get; init; } = "normal";

    [JsonPropertyName("generation_version")]
    public required string GenerationVersion { get; init; }
}

public static class AvatarManifestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Serialize(AvatarManifest manifest)
    {
        Validate(manifest);
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static AvatarManifest Deserialize(string json)
    {
        AvatarManifest manifest = JsonSerializer.Deserialize<AvatarManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Avatar manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    public static void Validate(AvatarManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported avatar schema: {manifest.SchemaVersion}.");
        }

        if (!Guid.TryParse(manifest.Id, out _))
        {
            throw new InvalidDataException("Avatar id must be a UUID.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80)
        {
            throw new InvalidDataException("Avatar name must contain 1 to 80 characters.");
        }

        if (manifest.HeightPx is < 64 or > 2_048)
        {
            throw new InvalidDataException("Avatar height is outside the supported range.");
        }

        if (Path.IsPathRooted(manifest.Rig) ||
            manifest.Rig.Split('/', '\\').Any(part => part == ".."))
        {
            throw new InvalidDataException("Rig path must stay inside the avatar directory.");
        }
    }
}

