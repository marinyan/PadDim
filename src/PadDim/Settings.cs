using System.Text.Json;

namespace PadDim;
public sealed record Settings
{
    public int IdleSeconds { get; init; } = 300;
    public int DimPercent { get; init; } = 65;
    public int DeadzonePercent { get; init; } = 12;
    public bool UseHardwareBrightness { get; init; } = true;
    public bool UseOverlayWithHardware { get; init; }
    // A distinct key avoids interpreting a legacy absolute setting as a relative ratio.
    public int BrightnessRatioPercent { get; init; } = 20;
    public int FadeSeconds { get; init; } = 3;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadDim", "settings.json");
    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        return s with { IdleSeconds = Math.Clamp(s.IdleSeconds, 10, 86400), DimPercent = Math.Clamp(s.DimPercent, 5, 90), DeadzonePercent = Math.Clamp(s.DeadzonePercent, 1, 40), BrightnessRatioPercent = Math.Clamp(s.BrightnessRatioPercent, 0, 100), FadeSeconds = Math.Clamp(s.FadeSeconds, 0, 60) };
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
