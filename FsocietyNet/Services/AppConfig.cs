using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsocietyNet.Services;

public enum TunnelMode { All, Include, Exclude }

public class TunnelApp
{
    public string Name    { get; set; } = "";
    public string ExePath { get; set; } = "";
}

/// <summary>Сохраняет пользовательские настройки в %AppData%\FsocietyNet\config.json</summary>
public class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FsocietyNet", "config.json");

    // ── Общие ──
    public bool KillSwitch    { get; set; } = false;
    public bool AutoStart     { get; set; } = false;
    public bool AutoConnect   { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public string? LastServerId { get; set; }

    // ── Туннелирование сайтов / IP ──
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TunnelMode SiteTunnelMode { get; set; } = TunnelMode.All;
    public List<string> TunnelSites  { get; set; } = [];  // IP / CIDR

    // ── Туннелирование приложений ──
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TunnelMode AppTunnelMode  { get; set; } = TunnelMode.All;
    public List<TunnelApp> TunnelApps { get; set; } = [];

    // ── Загрузка / сохранение ──

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new();
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented  = true,
        Converters     = { new JsonStringEnumConverter() }
    };
}
