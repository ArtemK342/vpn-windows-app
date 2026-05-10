using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsocietyNet.Services;

public record UpdateResult(bool HasUpdate, string? LatestVersion, string? ReleaseUrl);

public static class UpdateService
{
    private const string CurrentVersion = "0.1.0";
    private const string Repo           = "ArtemK342/vpn-windows-app";

    private static readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static UpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("FsocietyNet");
    }

    public static async Task<UpdateResult> CheckAsync()
    {
        try
        {
            var url  = $"https://api.github.com/repos/{Repo}/releases/latest";
            var json = await _client.GetStringAsync(url);
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName    = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl    = root.GetProperty("html_url").GetString()  ?? "";
            var latestVer  = tagName.TrimStart('v');

            if (IsNewer(latestVer, CurrentVersion))
                return new UpdateResult(true, latestVer, htmlUrl);
        }
        catch { }

        return new UpdateResult(false, null, null);
    }

    /// <summary>Returns true if candidate is strictly newer than current (semver compare).</summary>
    private static bool IsNewer(string candidate, string current)
    {
        try
        {
            return Version.Parse(candidate) > Version.Parse(current);
        }
        catch
        {
            return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
        }
    }
}
