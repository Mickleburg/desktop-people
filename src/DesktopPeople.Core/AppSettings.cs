using System.Text.Json;

namespace DesktopPeople.Core;

public sealed record AppSettings
{
    public int TargetFps { get; init; } = 60;

    public bool IsPaused { get; init; }

    public bool CharactersVisible { get; init; } = true;

    public string BehaviorIntensity { get; init; } = "normal";

    public bool ShowPlatformDebug { get; init; }

    public AppSettings Normalize() => this with
    {
        TargetFps = TargetFps is 30 or 60 ? TargetFps : 60,
        BehaviorIntensity = BehaviorIntensity is "calm" or "normal" or "active"
            ? BehaviorIntensity
            : "normal",
    };
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(_path);
            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Settings path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(temporaryPath, _path, true);
    }
}
