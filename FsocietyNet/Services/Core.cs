using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace FsocietyNet.Services;

public static class Paths
{
    /// <summary>%AppData%\FsocietyNet — settings, session (DPAPI), journal. Per user.</summary>
    public static readonly string UserDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FsocietyNet");

    /// <summary>
    /// %ProgramData%\FsocietyNet — VPN binaries and tunnel configs. Run as SYSTEM (tunnel service) or admin, so
    /// the folder is reset to Administrators/SYSTEM-only on every start: in %AppData% any user-level program
    /// could have swapped amneziawg.exe and got it started as a SYSTEM service.
    /// </summary>
    public static readonly string MachineDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FsocietyNet");
    public static string BinDir => Path.Combine(MachineDir, "bin");
    public static string RunDir => Path.Combine(MachineDir, "run");

    public static void EnsureUserDir() => Directory.CreateDirectory(UserDir);

    public static void SecureMachineDirs()
    {
        foreach (var d in new[] { MachineDir, BinDir, RunDir })
        {
            Directory.CreateDirectory(d);
            try
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);   // drop inherited (Users may create in ProgramData)
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                foreach (var sid in new[] { admins, system })
                    sec.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                        AccessControlType.Allow));
                sec.SetOwner(admins);
                new DirectoryInfo(d).SetAccessControl(sec);
            }
            catch (Exception e) { Log.E("acl " + d, e); }
        }
    }
}

public class TunnelApp
{
    public string Name { get; set; } = "";
    /// <summary>Executable name, e.g. Telegram.exe — sing-box matches the process by it.</summary>
    public string Exe { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>User settings, %AppData%\FsocietyNet\settings.json.</summary>
public class AppSettings
{
    public string Language { get; set; } = "";              // ru | en; empty = from Windows
    public string Theme { get; set; } = "dark";             // dark | light | system
    public bool KillSwitch { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public bool Autostart { get; set; } = false;            // start with Windows (scheduled task)
    public bool ConnectOnStart { get; set; } = false;       // connect to the last server when the app starts
    public bool CloseToTray { get; set; } = true;
    public string Dns { get; set; } = "cloudflare";
    public bool AllowLan { get; set; } = false;
    public string HysteriaMode { get; set; } = "bbr";       // bbr | brutal
    public double HysteriaDownMBs { get; set; } = 3.0;
    public double HysteriaUpMBs { get; set; } = 1.0;
    public string LogLevel { get; set; } = "off";           // off | min | max
    public string? LastServerId { get; set; }
    /// <summary>User pressed «disconnect» himself — then «connect on start» does not reconnect.</summary>
    public bool UserDisconnected { get; set; }
    public List<string> Favorites { get; set; } = [];

    // Split tunnelling (Rules): sites and apps independent, every mode keeps its own list (as on Android)
    public bool SitesEnabled { get; set; }
    public string SitesMode { get; set; } = "exclude";      // exclude | include
    public List<string> SitesExclude { get; set; } = [];
    public List<string> SitesInclude { get; set; } = [];
    public bool AppsEnabled { get; set; }
    public string AppsMode { get; set; } = "include";
    public List<TunnelApp> AppsExclude { get; set; } = [];
    public List<TunnelApp> AppsInclude { get; set; } = [];

    // «Режим РФ»: the system proxy settings before we set ours (restored on disconnect and after a crash)
    public string? ProxyBackup { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public List<string> Sites => SitesMode == "include" ? SitesInclude : SitesExclude;
    [System.Text.Json.Serialization.JsonIgnore] public List<TunnelApp> Apps => AppsMode == "include" ? AppsInclude : AppsExclude;
    [System.Text.Json.Serialization.JsonIgnore] public bool SitesActive => SitesEnabled && Sites.Count > 0;
    [System.Text.Json.Serialization.JsonIgnore] public bool AppsActive => AppsEnabled && Apps.Count > 0;
    public string RulesFingerprint() =>
        $"{SitesActive}|{SitesMode}|{string.Join(",", Sites)}|{AppsActive}|{AppsMode}|{string.Join(",", Apps.Select(a => a.Exe))}";

    static readonly string FilePath = Path.Combine(Paths.UserDir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new();
        }
        catch (Exception e) { Log.E("settings load", e); Current = new(); }
        if (Current.Language is not ("ru" or "en"))
            Current.Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "be" or "uk" or "kk"
                ? "ru" : "en";
    }

    /// <summary>`-render`: nothing is written to the user's real settings.</summary>
    public static bool ReadOnly;

    public static void Save()
    {
        if (ReadOnly) return;
        try
        {
            Paths.EnsureUserDir();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e) { Log.E("settings save", e); }
    }
}

public static class Loc
{
    public static string Lang => AppSettings.Current.Language == "en" ? "en" : "ru";
    public static bool En => Lang == "en";

    public static string Get(string key)
    {
        if (Strings.All.TryGetValue(key, out var v)) return En ? v.en : v.ru;
#if PREVIEW
        Missing.Add(key);
#endif
        return key;
    }
    public static string F(string key, params object[] args) => string.Format(Get(key), args);
#if PREVIEW
    public static readonly HashSet<string> Missing = [];
#endif

    public static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} {Get("unit.gb")}",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} {Get("unit.mb")}",
        >= 1L << 10 => $"{b / 1024.0:0} {Get("unit.kb")}",
        _ => $"{b} {Get("unit.b")}"
    };
    public static string Speed(double bytesPerSec) => Bytes((long)bytesPerSec) + Get("unit.persec");
}

/// <summary>
/// Journal for support (Settings → Journal), as on Android: off / min (connects, drops, errors, no sites) /
/// max (+ sing-box connection log). Keys, passwords, tokens, emails, account codes and UUIDs are cut out
/// before anything is written. Turning it off deletes the file.
/// </summary>
public static class Log
{
    public static string FilePath => Path.Combine(Paths.UserDir, "journal.log");
    static readonly object Gate = new();
    const long MaxBytes = 3 * 1024 * 1024;

    static readonly (Regex re, string to)[] Redactions =
    {
        (new(@"eyJ[\w-]+\.[\w-]+\.[\w-]+"), "<token>"),
        (new(@"[A-Za-z0-9+/]{42,43}="), "<key>"),
        (new(@"\b\d{4}-?\d{4}-?\d{4}-?\d{4}\b"), "<code>"),
        (new(@"[\w.+-]+@[\w-]+\.[\w.-]+"), "<email>"),
        (new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"), "<uuid>"),
        (new(@"(?i)(""?(password|passwd|auth|secret|private_key|privatekey|uuid|token)""?\s*[:=]\s*)""?[^"",\s}]+""?"), "$1<hidden>"),
        (new(@"vpn://\S+|vless://\S+|ss://\S+"), "<link>"),
    };

    public static string Redact(string s)
    {
        foreach (var (re, to) in Redactions) s = re.Replace(s, to);
        return s;
    }

    static bool On(bool detail) => AppSettings.Current.LogLevel switch
    {
        "max" => true,
        "min" => !detail,
        _ => false
    };

    public static void I(string msg) => Write(msg, false);
    public static void D(string msg) => Write(msg, true);
    public static void E(string where, Exception e) => Write($"ERROR {where}: {e.GetType().Name}: {e.Message}", false);

    static void Write(string msg, bool detail)
    {
#if DEBUG || PREVIEW
        System.Diagnostics.Debug.WriteLine(msg);
#endif
        if (!On(detail)) return;
        try
        {
            lock (Gate)
            {
                Paths.EnsureUserDir();
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var lines = File.ReadAllLines(FilePath);
                    File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Redact(msg)}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static long Size => File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
    public static void Clear() { lock (Gate) { try { File.Delete(FilePath); } catch { } } }

    public static string Header() =>
        $"FsocietyNet for Windows {App.Version} · {Environment.OSVersion.VersionString} · {(Environment.Is64BitOperatingSystem ? "x64" : "x86")} · {Loc.Lang}";
}

/// <summary>Access / refresh tokens and the cached profile, encrypted with DPAPI for this Windows user.</summary>
public static class Session
{
    static readonly string FilePath = Path.Combine(Paths.UserDir, "session.bin");
    record Stored(string access, string refresh, UserResponse? me, SubscriptionResponse? sub = null, bool subKnown = false);

    public static string Access { get; private set; } = "";
    public static string Refresh { get; private set; } = "";
    public static UserResponse? Me { get; private set; }
    /// <summary>Last known subscription (null = none); Settings shows it at once and refreshes it.</summary>
    public static SubscriptionResponse? Sub { get; private set; }
    public static bool SubKnown { get; private set; }
    public static bool SignedIn => Access.Length > 0;

    /// <summary>Refresh failed: the UI goes back to the login screen.</summary>
    public static event Action? Expired;

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(FilePath)));
            var s = JsonSerializer.Deserialize<Stored>(json);
            if (s == null) return;
            Access = s.access ?? ""; Refresh = s.refresh ?? ""; Me = s.me; Sub = s.sub; SubKnown = s.subKnown;
        }
        catch (Exception e) { Log.E("session load", e); }
        // Migration: old versions kept tokens as plain text files
        foreach (var old in new[] { "token.txt", "refresh_token.txt" })
            try { File.Delete(Path.Combine(Paths.UserDir, old)); } catch { }
    }

    public static void Set(string access, string? refresh)
    {
        Access = access;
        if (!string.IsNullOrEmpty(refresh)) Refresh = refresh;
        Persist();
    }

    public static void SetMe(UserResponse me) { Me = me; Persist(); }
    public static void SetSub(SubscriptionResponse? sub) { Sub = sub; SubKnown = true; Persist(); }

    static bool _demo;

    /// <summary>`-render`: a signed-in (or signed-out) session in memory only.</summary>
    public static void SetDemo(bool signedIn)
    {
        _demo = true;
        Access = signedIn ? "demo" : ""; Refresh = Access;
        Me = signedIn ? Demo.Me() : null;
    }

    static void Persist()
    {
        if (_demo || Api.DemoMode) return;
        try
        {
            Paths.EnsureUserDir();
            var json = JsonSerializer.Serialize(new Stored(Access, Refresh, Me, Sub, SubKnown));
            File.WriteAllBytes(FilePath, Dpapi.Protect(Encoding.UTF8.GetBytes(json)));
        }
        catch (Exception e) { Log.E("session save", e); }
    }

    public static void Clear()
    {
        Access = ""; Refresh = ""; Me = null; Sub = null; SubKnown = false;
        try { File.Delete(FilePath); } catch { }
    }

    public static void Expire()
    {
        Clear();
        Application.Current?.Dispatcher.BeginInvoke(() => Expired?.Invoke());
    }

    /// <summary>Anonymous account: token subject «a:&lt;id&gt;» (or the old «n:&lt;code&gt;»).</summary>
    public static bool IsAnonymous
    {
        get
        {
            if (Me?.account_type == "numeric") return true;
            var sub = JwtSubject(Access);
            return sub != null && (sub.StartsWith("a:") || sub.StartsWith("n:"));
        }
    }

    static string? JwtSubject(string token)
    {
        try
        {
            var p = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
            return doc.RootElement.TryGetProperty("sub", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }
}

static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int cb; public IntPtr pb; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? descr, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr descr, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr h);

    const int UiForbidden = 0x1;

    public static byte[] Protect(byte[] data) => Run(data, true);
    public static byte[] Unprotect(byte[] data) => Run(data, false);

    static byte[] Run(byte[] data, bool protect)
    {
        var input = new Blob { cb = data.Length, pb = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.pb, data.Length);
            var ok = protect
                ? CryptProtectData(ref input, "FsocietyNet", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.cb];
            Marshal.Copy(output.pb, result, 0, output.cb);
            LocalFree(output.pb);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.pb); }
    }
}

/// <summary>Dark / light / system theme: swaps the first merged dictionary (all styles use DynamicResource).</summary>
public static class Theme
{
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    public static void Apply()
    {
        var mode = AppSettings.Current.Theme;
        IsDark = mode switch { "light" => false, "system" => SystemIsDark(), _ => true };
        var md = Application.Current.Resources.MergedDictionaries;
        md[0] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        Changed?.Invoke();
    }

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }

    public static void WatchSystem()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && AppSettings.Current.Theme == "system")
                Application.Current.Dispatcher.BeginInvoke(Apply);
        };
    }

    // ── Windows 11 frame: rounded corners and a border in the theme colour ──
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void StyleFrame(IntPtr hwnd)
    {
        try
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4);          // DWMWA_USE_IMMERSIVE_DARK_MODE
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);         // DWMWA_WINDOW_CORNER_PREFERENCE = round
            int border = IsDark ? 0x002A2625 : 0x00D9E0E0;         // COLORREF 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);        // DWMWA_BORDER_COLOR
        }
        catch { }
    }
}
