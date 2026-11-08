using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FsocietyNet.Services;

public record ZapretTarget(string Name, string? Url, string? PingHost);
public record ZapretTestResult(string Strategy, int Ok, int Total, string Details);
/// <summary>Diagnostics line: level ok | warn | bad.</summary>
public record DiagItem(string Level, string Text);
public record GameFilter(string Mode, string Tcp, string Udp);   // mode: disabled | all | tcp | udp

/// <summary>
/// zapret-discord-youtube (Flowseal) 1.10.4 embedded as its official release zip and driven from the app:
/// the same files and the same actions as its service.bat menu (service, game filter, IPSet, auto-update check,
/// active fakes, IPSet / hosts update, update check, diagnostics, tests), but as buttons. Lives in
/// %ProgramData%\FsocietyNet\zapret — admin-only, no Cyrillic / OneDrive in the path (zapret's own diagnostics
/// warn about both). The Windows service keeps service.bat's names (zapret, registry value
/// zapret-discord-youtube), so the original tools see it too.
/// </summary>
public static class Zapret
{
    public const string Version = "1.10.4";
    const string Resource = "bin.zapret.zip";
    const string ServiceName = "zapret";
    const string RegValue = "zapret-discord-youtube";
    const string Repo = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube";
    public const string ReleasesUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/latest";

    // Files of the official zip (sha256), checked before every start
    static readonly Dictionary<string, string> Pinned = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winws.exe"] = "AFFB4F69D2EA302A7ABCCD5325D81826E140DDAE014F1E070BC4A6C0DD555188",
        ["WinDivert.dll"] = "C1E060EE19444A259B2162F8AF0F3FE8C4428A1C6F694DCE20DE194AC8D7D9A2",
        ["WinDivert64.sys"] = "8DA085332782708D8767BCACE5327A6EC7283C17CFB85E40B03CD2323A90DDC2",
        ["cygwin1.dll"] = "103104A52E5293CE418944725DF19E2BF81AD9269B9A120D71D39028E821499B",
    };

    // The user's settings and lists survive an update of the embedded zapret
    static readonly HashSet<string> Keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "lists/list-general-user.txt", "lists/list-exclude-user.txt", "lists/ipset-exclude-user.txt",
        "lists/ipset-all.txt", "lists/ipset-all.txt.backup", "utils/game_filter.enabled", "utils/check_updates.enabled",
        "bin/ACTIVE_DISCORD_UDP.bin", "bin/ACTIVE_GAME_UDP.bin",
    };

    /// <summary>`-render`: extract into a scratch folder to check the strategy parsing (nothing is started).</summary>
    internal static string? RootOverride;
    public static string Root => RootOverride ?? Path.Combine(Paths.MachineDir, "zapret");
    public static string BinDir => Path.Combine(Root, "bin");
    public static string ListsDir => Path.Combine(Root, "lists");
    static string UtilsDir => Path.Combine(Root, "utils");
    static string Winws => Path.Combine(BinDir, "winws.exe");
    static string IpsetFile => Path.Combine(ListsDir, "ipset-all.txt");
    static string GameFile => Path.Combine(UtilsDir, "game_filter.enabled");
    static string UpdatesFlag => Path.Combine(UtilsDir, "check_updates.enabled");
    const string IpsetDummy = "203.0.113.113/32";

    static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
    static Process? _proc;

    public static bool Embedded => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(Resource);

    /// <summary>«Always, for the whole system»: the zapret service does the bypass, the server row is hidden.</summary>
    public static bool SystemMode =>
        AppSettings.Current.BypassMode == "system" && AppSettings.Current.BypassEngine != "byedpi" && Embedded;

    /// <summary>Bypass mode / service changed in Settings: the home screen updates its list and status line.</summary>
    public static event Action? ModeChanged;
    public static void NotifyModeChanged() => ModeChanged?.Invoke();

    public static bool ServiceRunning => ServiceState(ServiceName) == ServiceControllerStatus.Running;

    // ═══ Files ═══

    public static void Ensure()
    {
        if (!Embedded) throw new EngineException("err.no_binary", "zapret");
        if (RootOverride == null) Paths.SecureMachineDirs();
        var marker = Path.Combine(Root, "version.txt");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != Version || !File.Exists(Winws))
        {
            Extract(keepUserFiles: File.Exists(marker));
            File.WriteAllText(marker, Version);
            Log.I("zapret: extracted " + Version);
        }
        LoadUserLists();
        foreach (var (name, sha) in Pinned)
        {
            if (HashOk(Path.Combine(BinDir, name), sha)) continue;
            Extract(keepUserFiles: true);
            if (!HashOk(Path.Combine(BinDir, name), sha)) throw new InvalidDataException("zapret " + name + ": hash mismatch");
        }
    }

    static bool HashOk(string path, string sha)
    {
        try { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).Equals(sha, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static void Extract(bool keepUserFiles)
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)!;
        using var zip = new ZipArchive(res, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            var slash = e.FullName.IndexOf('/');
            var rel = slash >= 0 ? e.FullName[(slash + 1)..] : e.FullName;   // drop the top folder
            if (rel.Length == 0 || rel.EndsWith('/')) continue;
            var dst = Path.Combine(Root, rel.Replace('/', '\\'));
            if (keepUserFiles && Keep.Contains(rel) && File.Exists(dst)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            e.ExtractToFile(dst, true);
        }
    }

    /// <summary>service.bat :load_user_lists — the user lists must exist and never be empty.</summary>
    static void LoadUserLists()
    {
        void Make(string name, string text) { var p = Path.Combine(ListsDir, name); if (!File.Exists(p)) File.WriteAllText(p, text); }
        Make("ipset-exclude-user.txt", IpsetDummy + "\r\n");
        Make("list-general-user.txt", "# Never leave this file empty\r\ndomain.example.abc\r\n");
        Make("list-exclude-user.txt", "domain.example.abc\r\n");
    }

    public static string UserListPath(string name) => Path.Combine(ListsDir, name);

    // ═══ Strategies (general*.bat) ═══

    public static List<string> Strategies()
    {
        IEnumerable<string> names;
        try { names = Directory.GetFiles(Root, "*.bat").Select(f => Path.GetFileNameWithoutExtension(f)); }
        catch
        {
            // not extracted yet (or the preview build without admin rights): names from the embedded zip
            if (!Embedded) return [];
            using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)!;
            using var zip = new ZipArchive(res, ZipArchiveMode.Read);
            names = zip.Entries.Where(e => e.FullName.Count(c => c == '/') == 1 && e.Name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                .Select(e => Path.GetFileNameWithoutExtension(e.Name)).ToList();
        }
        return names
            .Where(n => !n.StartsWith("service", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => Regex.Replace(n, @"\d+", m => m.Value.PadLeft(8, '0')), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// winws.exe arguments of a strategy file, like service.bat :service_install: the command from «winws.exe"» to the
    /// end of the ^-continued lines, with %BIN% / %LISTS% / %~dp0 and the game filter ports substituted.
    /// </summary>
    public static List<string> Args(string strategy)
    {
        var text = File.ReadAllText(Path.Combine(Root, strategy + ".bat"));
        var sb = new StringBuilder();
        var capture = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (!capture)
            {
                var i = line.IndexOf("winws.exe\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                capture = true;
                line = line[(i + "winws.exe\"".Length)..];
            }
            var more = line.EndsWith('^');
            if (more) line = line[..^1];
            sb.Append(line).Append(' ');
            if (!more) break;
        }
        var (tcp, udp) = GameFilterPorts();
        var cmd = sb.ToString()
            .Replace("%BIN%", BinDir + "\\").Replace("%LISTS%", ListsDir + "\\")
            .Replace("%GameFilterTCP%", tcp).Replace("%GameFilterUDP%", udp).Replace("%GameFilter%", tcp)
            .Replace("%~dp0", Root + "\\");
        return Tokenize(cmd);
    }

    /// <summary>cmd-like split: spaces separate, quotes group (and are dropped), ^ escapes the next character.</summary>
    static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && c == '^' && i + 1 < s.Length) { cur.Append(s[++i]); any = true; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (any || cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); any = false; }
                continue;
            }
            cur.Append(c);
        }
        if (any || cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    static string Quote(string a) =>
        a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    // ═══ Run (bypass connected from the app) ═══

    public static bool WinwsRunning() => Process.GetProcessesByName("winws").Length > 0;

    /// <summary>winws started by a crashed run of the app (not by the zapret service): stopped at start.</summary>
    public static void KillOrphans()
    {
        if (ServiceState(ServiceName) == ServiceControllerStatus.Running) return;
        foreach (var p in Process.GetProcessesByName("winws"))
        {
            try { if ((p.MainModule?.FileName ?? "").StartsWith(Root, StringComparison.OrdinalIgnoreCase)) p.Kill(true); } catch { }
        }
    }
    public static bool OurProcessAlive => _proc is { HasExited: false };

    public static async Task<bool> Start(string strategy)
    {
        Ensure();
        StopOurs();
        await EnableTcpTimestamps();
        var psi = new ProcessStartInfo(Winws)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = BinDir,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in Args(strategy)) psi.ArgumentList.Add(a);
        var p = Process.Start(psi);
        if (p == null) return false;
        var tail = new Queue<string>();
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 20) tail.Dequeue(); }
            Log.D("winws: " + e.Data);
        }
        p.OutputDataReceived += OnLine; p.ErrorDataReceived += OnLine;
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        await Task.Delay(1500);
        if (p.HasExited)
        {
            lock (tail) Log.I($"winws exited {p.ExitCode}: " + string.Join(" | ", tail));
            return false;
        }
        _proc = p;
        Log.I("zapret started: " + strategy);
        return true;
    }

    public static void StopOurs()
    {
        try { if (_proc is { HasExited: false }) { _proc.Kill(true); _proc.WaitForExit(3000); } } catch { }
        _proc = null;
    }

    static void KillAllWinws()
    {
        foreach (var p in Process.GetProcessesByName("winws")) { try { p.Kill(true); p.WaitForExit(3000); } catch { } }
        _proc = null;
    }

    /// <summary>service.bat :tcp_enable — zapret needs TCP timestamps.</summary>
    public static async Task EnableTcpTimestamps()
    {
        var (_, o) = await NetshTcpGlobal();
        if (Regex.IsMatch(o, @"timestamps\s*:\s*enabled", RegexOptions.IgnoreCase)) return;
        await Proc.Run("netsh", "interface tcp set global timestamps=enabled");
    }

    // ═══ SERVICE: 1. Install · 2. Remove · 3. Status ═══

    public static ServiceControllerStatus? ServiceState(string name)
    {
        try { using var sc = new ServiceController(name); return sc.Status; }
        catch { return null; }
    }

    public static string? InstalledStrategy()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\" + ServiceName);
            return k?.GetValue(RegValue) as string;
        }
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern int GetOEMCP();

    /// <summary>Console tools write in the OEM code page (866 on Russian Windows): read as UTF-8 it was «гбПе».</summary>
    static Encoding Oem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(GetOEMCP()); } catch { return Encoding.UTF8; }
    }

    static async Task<(int code, string output)> ScRun(params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Oem(), StandardErrorEncoding = Oem(),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, o.Trim());
    }

    static async Task<string> Sc(params string[] args) => (await ScRun(args)).output;

    /// <summary>netsh in English whatever the Windows language (service.bat does «chcp 437» for the same reason).</summary>
    static Task<(int code, string output)> NetshTcpGlobal() =>
        Proc.Run("cmd.exe", "/c chcp 437 >nul & netsh interface tcp show global");

    static async Task StopService(string name)
    {
        if (ServiceState(name) is not { } st || st == ServiceControllerStatus.Stopped) return;
        await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            catch { }
        });
    }

    public static async Task<string> InstallService(string strategy)
    {
        Ensure();
        StopOurs();
        await EnableTcpTimestamps();
        var cmdline = Quote(Winws) + " " + string.Join(" ", Args(strategy).Select(Quote));
        await StopService(ServiceName);
        await Sc("delete", ServiceName);
        var log = new StringBuilder();
        log.AppendLine(await Sc("create", ServiceName, "binPath=", cmdline, "DisplayName=", "zapret", "start=", "auto"));
        await Sc("description", ServiceName, "Zapret DPI bypass software");
        log.AppendLine(await Sc("start", ServiceName));
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Services\" + ServiceName);
            k.SetValue(RegValue, strategy);
        }
        catch (Exception e) { log.AppendLine(e.Message); }
        Log.I("zapret service installed: " + strategy);
        return log.ToString();
    }

    /// <summary>service.bat «Remove Services»: the zapret service, winws.exe and the WinDivert driver — as readable lines.</summary>
    public static async Task<List<DiagItem>> RemoveServices()
    {
        var list = new List<DiagItem>();
        if (ServiceState(ServiceName) != null)
        {
            await StopService(ServiceName);
            var (code, o) = await ScRun("delete", ServiceName);
            list.Add(code == 0 ? new("ok", Loc.Get("zapret.service_removed")) : new("bad", Loc.F("zapret.remove_failed", ServiceName, o)));
        }
        else list.Add(new("ok", Loc.Get("zapret.service_not_installed")));
        if (WinwsRunning()) { KillAllWinws(); list.Add(new("ok", Loc.Get("zapret.winws_stopped"))); }
        foreach (var d in new[] { "WinDivert", "WinDivert14" })
        {
            if (ServiceState(d) == null) continue;
            await StopService(d);
            var (code, o) = await ScRun("delete", d);
            list.Add(code == 0 ? new("ok", Loc.F("zapret.driver_removed", d)) : new("bad", Loc.F("zapret.remove_failed", d, o)));
        }
        Log.I("zapret services removed");
        return list;
    }

    public static List<DiagItem> Status()
    {
        var list = new List<DiagItem>();
        if (InstalledStrategy() is { } s) list.Add(new("ok", Loc.F("zapret.status_strategy", s)));
        void Svc(string name)
        {
            var st = ServiceState(name);
            list.Add(st == ServiceControllerStatus.Running ? new("ok", Loc.F("zapret.svc_running", name))
                : st == ServiceControllerStatus.StopPending ? new("warn", Loc.F("zapret.svc_stop_pending", name))
                : new("warn", Loc.F("zapret.svc_not_running", name)));
        }
        Svc(ServiceName);
        Svc("WinDivert");
        if (!File.Exists(Path.Combine(BinDir, "WinDivert64.sys"))) list.Add(new("bad", Loc.Get("zapret.no_sys")));
        list.Add(WinwsRunning() ? new("ok", Loc.Get("zapret.winws_running")) : new("bad", Loc.Get("zapret.winws_not_running")));
        return list;
    }

    // ═══ SETTINGS: 4. Game filter · 5. IPSet · 6. Auto-update check · 7. Active fakes ═══

    public static GameFilter ReadGameFilter()
    {
        string mode = "disabled", tcp = "1024-65535", udp = "1024-65535";
        try
        {
            if (File.Exists(GameFile))
                foreach (var line in File.ReadAllLines(GameFile))
                {
                    var kv = line.Split('=', 2);
                    var k = kv[0].Trim().ToLowerInvariant();
                    var v = kv.Length > 1 ? kv[1].Trim() : "";
                    if (k == "mode") mode = v;
                    else if (k == "all") mode = "all";
                    else if (k == "tcp") { if (v.Length == 0) mode = "tcp"; else if (ValidPorts(v)) tcp = v; }
                    else if (k == "udp") { if (v.Length == 0) mode = "udp"; else if (ValidPorts(v)) udp = v; }
                }
        }
        catch { }
        if (mode is not ("all" or "tcp" or "udp")) mode = "disabled";
        return new(mode, tcp, udp);
    }

    public static void SaveGameFilter(GameFilter g)
    {
        Directory.CreateDirectory(UtilsDir);
        File.WriteAllText(GameFile, $"mode={g.Mode}\r\ntcp={g.Tcp}\r\nudp={g.Udp}\r\n");
    }

    /// <summary>Like service.bat :validate_game_filter_range: 1024-65535 or 1024-1934,1936-65535.</summary>
    public static bool ValidPorts(string s)
    {
        s = s.Replace(" ", "");
        if (s.Length == 0) return false;
        foreach (var item in s.Split(','))
        {
            var m = Regex.Match(item, @"^([1-9]\d{0,4})(?:-([1-9]\d{0,4}))?$");
            if (!m.Success) return false;
            int a = int.Parse(m.Groups[1].Value), b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : a;
            if (a > 65535 || b > 65535 || a > b) return false;
        }
        return true;
    }

    /// <summary>Ports put into %GameFilterTCP% / %GameFilterUDP% («12» = the filter is off, as in service.bat).</summary>
    static (string tcp, string udp) GameFilterPorts()
    {
        var g = ReadGameFilter();
        return g.Mode switch
        {
            "all" => (g.Tcp, g.Udp),
            "tcp" => (g.Tcp, "12"),
            "udp" => ("12", g.Udp),
            _ => ("12", "12")
        };
    }

    /// <summary>loaded = the IP list is used · none = only domains · any = every IP.</summary>
    public static string IpsetStatus()
    {
        try
        {
            if (!File.Exists(IpsetFile)) return "any";
            var lines = File.ReadAllLines(IpsetFile);
            if (lines.Length == 0) return "any";
            return lines.Any(l => l.Contains(IpsetDummy)) ? "none" : "loaded";
        }
        catch { return "any"; }
    }

    /// <summary>service.bat :ipset_switch — loaded → none → any → loaded. false = no backup to restore.</summary>
    public static bool CycleIpset()
    {
        var backup = IpsetFile + ".backup";
        switch (IpsetStatus())
        {
            case "loaded":
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(IpsetFile, backup);
                File.WriteAllText(IpsetFile, IpsetDummy + "\r\n");
                return true;
            case "none":
                File.WriteAllText(IpsetFile, "");
                return true;
            default:
                if (!File.Exists(backup)) return false;
                File.Delete(IpsetFile);
                File.Move(backup, IpsetFile);
                return true;
        }
    }

    public static bool UpdateCheckEnabled => File.Exists(UpdatesFlag);
    public static void SetUpdateCheck(bool on)
    {
        Directory.CreateDirectory(UtilsDir);
        if (on) File.WriteAllText(UpdatesFlag, "ENABLED\r\n"); else if (File.Exists(UpdatesFlag)) File.Delete(UpdatesFlag);
    }

    /// <summary>Fake payloads in bin (*.bin except ACTIVE_*) and which one each active fake is now (by hash).</summary>
    public static (List<string> fakes, string? discord, string? game) Fakes()
    {
        string H(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)); }
        var files = Directory.GetFiles(BinDir, "*.bin").Where(f => !Path.GetFileName(f).StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var hashes = files.ToDictionary(f => f, H);
        string? Current(string active)
        {
            var p = Path.Combine(BinDir, active);
            if (!File.Exists(p)) return null;
            var h = H(p);
            return hashes.FirstOrDefault(kv => kv.Value == h).Key is { } f ? Path.GetFileNameWithoutExtension(f) : null;
        }
        return (files.Select(f => Path.GetFileNameWithoutExtension(f)!).ToList(), Current("ACTIVE_DISCORD_UDP.bin"), Current("ACTIVE_GAME_UDP.bin"));
    }

    /// <summary>type: discord | game.</summary>
    public static void ReplaceFake(string type, string fake) =>
        File.Copy(Path.Combine(BinDir, fake + ".bin"), Path.Combine(BinDir, type == "game" ? "ACTIVE_GAME_UDP.bin" : "ACTIVE_DISCORD_UDP.bin"), true);

    // ═══ UPDATES: 8. IPSet list · 9. Hosts · 10. Check for updates ═══

    public static async Task UpdateIpset()
    {
        var text = await Http.GetStringAsync(Repo + "/refs/heads/main/.service/ipset-service.txt");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("empty list");
        File.WriteAllText(IpsetFile, text);
        Log.I("zapret: ipset updated");
    }

    /// <summary>The repository hosts block; needsUpdate = its first or last line is missing in the system hosts.</summary>
    public static async Task<(bool needsUpdate, string content)> CheckHosts()
    {
        var content = await Http.GetStringAsync(Repo + "/refs/heads/main/.service/hosts?t=" + Random.Shared.Next());
        var lines = content.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        var hosts = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
        var needs = lines.Count > 0 && (!hosts.Contains(lines[0]) || !hosts.Contains(lines[^1]));
        return (needs, content);
    }

    public static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\drivers\etc\hosts");

    public static async Task<string?> LatestVersion()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Repo + "/main/.service/version.txt");
        req.Headers.CacheControl = new() { NoCache = true };
        using var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode ? (await resp.Content.ReadAsStringAsync()).Trim() : null;
    }

    // ═══ TOOLS: 11. Diagnostics · 12. Tests ═══

    static readonly string[] ConflictServices = ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"];

    public static List<string> FoundConflicts() => ConflictServices.Where(s => ServiceState(s) != null).ToList();

    public static async Task<List<DiagItem>> Diagnostics()
    {
        var list = new List<DiagItem>();
        var services = await Proc.Run("cmd.exe", "/c chcp 437 >nul & sc query type= all state= all");
        var svcText = services.output;
        bool Has(string pattern) => Regex.IsMatch(svcText, pattern, RegexOptions.IgnoreCase);

        list.Add(ServiceState("BFE") == ServiceControllerStatus.Running ? new("ok", Loc.Get("diag.bfe_ok")) : new("bad", Loc.Get("diag.bfe_bad")));

        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (k?.GetValue("ProxyEnable") is int pe && pe == 1) list.Add(new("warn", Loc.F("diag.proxy", k.GetValue("ProxyServer") as string ?? "")));
            else list.Add(new("ok", Loc.Get("diag.proxy_ok")));
        }
        catch { }

        var (_, tcp) = await NetshTcpGlobal();
        if (Regex.IsMatch(tcp, @"timestamps\s*:\s*enabled", RegexOptions.IgnoreCase)) list.Add(new("ok", Loc.Get("diag.ts_ok")));
        else
        {
            await Proc.Run("netsh", "interface tcp set global timestamps=enabled");
            list.Add(new("warn", Loc.Get("diag.ts_fixed")));
        }

        list.Add(Process.GetProcessesByName("AdguardSvc").Length > 0 ? new("bad", Loc.Get("diag.adguard")) : new("ok", Loc.Get("diag.adguard_ok")));
        list.Add(Has(@"SERVICE_NAME:.*Killer") ? new("bad", Loc.Get("diag.killer")) : new("ok", Loc.Get("diag.killer_ok")));
        list.Add(Regex.IsMatch(svcText, @"Intel.*Connectivity.*Network", RegexOptions.IgnoreCase) ? new("bad", Loc.Get("diag.intel")) : new("ok", Loc.Get("diag.intel_ok")));
        list.Add(Has("TracSrvWrapper") || Has("EPWD") ? new("bad", Loc.Get("diag.checkpoint")) : new("ok", Loc.Get("diag.checkpoint_ok")));
        list.Add(Has("SmartByte") ? new("bad", Loc.Get("diag.smartbyte")) : new("ok", Loc.Get("diag.smartbyte_ok")));
        if (!File.Exists(Path.Combine(BinDir, "WinDivert64.sys"))) list.Add(new("bad", Loc.Get("zapret.no_sys")));

        var vpn = Regex.Matches(svcText, @"SERVICE_NAME:\s*(\S*(?:VPN|HappService)\S*)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).Distinct().ToList();
        list.Add(vpn.Count > 0 ? new("warn", Loc.F("diag.vpn", string.Join(", ", vpn))) : new("ok", Loc.Get("diag.vpn_ok")));

        // Secure DNS set in Windows: any key below InterfaceSpecificParameters with DohFlags > 0
        static bool DohIn(RegistryKey? k, int depth)
        {
            if (k == null || depth > 6) return false;
            if (k.GetValue("DohFlags") is int f && f > 0) return true;
            foreach (var n in k.GetSubKeyNames())
            {
                using var sub = k.OpenSubKey(n);
                if (DohIn(sub, depth + 1)) return true;
            }
            return false;
        }
        bool doh;
        try
        {
            using var dohRoot = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            doh = DohIn(dohRoot, 0);
        }
        catch { doh = false; }
        list.Add(doh ? new("ok", Loc.Get("diag.doh_ok")) : new("warn", Loc.Get("diag.doh")));

        try
        {
            var hosts = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
            if (Regex.IsMatch(hosts, @"youtube\.com|youtu\.be", RegexOptions.IgnoreCase)) list.Add(new("warn", Loc.Get("diag.hosts_yt")));
        }
        catch { }

        // WinDivert left without winws (another bypass or a crash): service.bat removes it
        var wd = ServiceState("WinDivert");
        if (!WinwsRunning() && wd is ServiceControllerStatus.Running or ServiceControllerStatus.StopPending)
        {
            await StopService("WinDivert");
            await Sc("delete", "WinDivert");
            list.Add(ServiceState("WinDivert") == null ? new("ok", Loc.Get("diag.windivert_removed")) : new("bad", Loc.Get("diag.windivert_stuck")));
        }

        var conflicts = FoundConflicts();
        if (conflicts.Count > 0) list.Add(new("bad", Loc.F("diag.conflicts", string.Join(", ", conflicts))));
        return list;
    }

    public static async Task<string> RemoveConflicts()
    {
        var log = new StringBuilder();
        foreach (var s in FoundConflicts())
        {
            await StopService(s);
            log.AppendLine($"{s}: {await Sc("delete", s)}");
        }
        foreach (var d in new[] { "WinDivert", "WinDivert14" }) { await StopService(d); await Sc("delete", d); }
        return log.ToString().Trim();
    }

    /// <summary>service.bat :clear_discord_cache — closes Discord and deletes Cache, Code Cache, GPUCache.</summary>
    public static List<DiagItem> ClearDiscordCache()
    {
        var list = new List<DiagItem>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var found = false;
        foreach (var (exe, dir) in new[] { ("Discord", "discord"), ("DiscordPTB", "discordptb"), ("DiscordCanary", "discordcanary"), ("DiscordDevelopment", "discorddevelopment") })
        {
            var path = Path.Combine(appData, dir);
            if (!Directory.Exists(path)) continue;
            found = true;
            foreach (var p in Process.GetProcessesByName(exe)) { try { p.Kill(true); p.WaitForExit(3000); } catch { } }
            foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" })
            {
                var d = Path.Combine(path, c);
                if (!Directory.Exists(d)) continue;
                try { Directory.Delete(d, true); list.Add(new("ok", Loc.F("diag.deleted", d))); }
                catch { list.Add(new("bad", Loc.F("diag.delete_failed", d))); }
            }
        }
        if (!found) list.Add(new("bad", Loc.Get("diag.no_discord")));
        return list;
    }

    public static List<ZapretTarget> Targets()
    {
        var file = Path.Combine(UtilsDir, "targets.txt");
        var list = new List<ZapretTarget>();
        if (!File.Exists(file)) return list;
        foreach (var line in File.ReadAllLines(file))
        {
            var m = Regex.Match(line, @"^\s*(\w+)\s*=\s*""([^""]+)""");
            if (!m.Success) continue;
            var v = m.Groups[2].Value.Trim();
            if (v.StartsWith("PING:", StringComparison.OrdinalIgnoreCase)) list.Add(new(m.Groups[1].Value, null, v[5..].Trim()));
            else list.Add(new(m.Groups[1].Value, v, new Uri(v).Host));
        }
        return list;
    }

    /// <summary>
    /// Like «test zapret.ps1» (standard tests): every strategy in turn — start winws, then HTTP / TLS 1.2 / TLS 1.3
    /// requests to the sites of utils/targets.txt plus ping; result = how many checks passed. The zapret service and
    /// a running bypass are stopped for the test and the service is started again afterwards.
    /// </summary>
    public static async Task<List<ZapretTestResult>> RunTests(IList<string> strategies, Action<string, int, int> progress, CancellationToken ct)
    {
        Ensure();
        var targets = Targets();
        var results = new List<ZapretTestResult>();
        var serviceWasRunning = ServiceState(ServiceName) == ServiceControllerStatus.Running;
        if (serviceWasRunning) await StopService(ServiceName);
        KillAllWinws();
        try
        {
            for (int i = 0; i < strategies.Count && !ct.IsCancellationRequested; i++)
            {
                var s = strategies[i];
                progress(s, i, strategies.Count);
                if (!await Start(s)) { results.Add(new(s, 0, 0, Loc.Get("zapret.test_start_failed"))); continue; }
                var checks = targets.Select(t => Check(t, ct)).ToList();
                var r = await Task.WhenAll(checks);
                StopOurs();
                var ok = r.Sum(x => x.ok);
                var total = r.Sum(x => x.total);
                results.Add(new(s, ok, total, string.Join("\n", r.Select(x => x.line))));
                await Task.Delay(300, CancellationToken.None);
            }
        }
        finally
        {
            StopOurs();
            if (serviceWasRunning) { try { await Sc("start", ServiceName); } catch { } }
        }
        return results.OrderByDescending(r => r.Total == 0 ? -1 : r.Ok * 1.0 / r.Total).ThenBy(r => strategies.IndexOf(r.Strategy)).ToList();
    }

    static async Task<(int ok, int total, string line)> Check(ZapretTarget t, CancellationToken ct)
    {
        int ok = 0, total = 0;
        var parts = new List<string>();
        if (t.Url != null)
        {
            foreach (var (label, proto) in new[] { ("HTTP", SslProtocols.None), ("TLS1.2", SslProtocols.Tls12), ("TLS1.3", SslProtocols.Tls13) })
            {
                total++;
                try
                {
                    using var h = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2), AllowAutoRedirect = false };
                    if (proto != SslProtocols.None) h.SslOptions.EnabledSslProtocols = proto;
                    using var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(5) };
                    using var req = new HttpRequestMessage(HttpMethod.Head, t.Url) { Version = System.Net.HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                    using var resp = await c.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    ok++;
                    parts.Add($"{label}:OK");
                }
                catch (HttpRequestException e) when (e.InnerException is AuthenticationException) { parts.Add($"{label}:SSL"); }
                catch (NotSupportedException) { total--; parts.Add($"{label}:UNSUP"); }
                catch { parts.Add($"{label}:ERROR"); }
            }
        }
        var ping = "n/a";
        if (t.PingHost != null)
        {
            try
            {
                using var p = new Ping();
                var reply = await p.SendPingAsync(t.PingHost, 1000);
                ping = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "Timeout";
            }
            catch { ping = "Timeout"; }
            if (t.Url == null) { total++; if (ping != "Timeout") ok++; }
        }
        return (ok, total, $"{t.Name,-22} {string.Join("  ", parts)}  ping {ping}");
    }
}

/// <summary>«Режим РФ» through zapret (Settings → Bypass → engine = zapret): winws.exe for the whole system.</summary>
public sealed class ZapretEngine : IEngine
{
    public string Kind => "bypass";
    bool _viaService;
    string _strategy = "general";

    public async Task Start(string strategy)
    {
        _strategy = strategy;
        // Installed as a Windows service and running: it already does the job, a second winws would conflict
        if (Zapret.ServiceState("zapret") == System.ServiceProcess.ServiceControllerStatus.Running) { _viaService = true; return; }
        if (!await Zapret.Start(strategy)) throw new EngineException("zapret.error_start");
        // Auto-Update Check (service.bat checks at every start): a newer zapret release is only announced —
        // ours is embedded and comes with an app update
        if (Zapret.UpdateCheckEnabled)
            _ = Task.Run(async () =>
            {
                try
                {
                    var v = await Zapret.LatestVersion();
                    if (v != null && v != Zapret.Version)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            Tray.Notify(Loc.F("bypass.updates_new_short", v));
                            if (Views.MainWindow.Instance?.IsVisible == true) Views.Dialogs.Toast(Loc.F("bypass.updates_new_short", v));
                        });
                }
                catch { }
            });
    }

    public bool Alive() => _viaService ? Zapret.ServiceState("zapret") == System.ServiceProcess.ServiceControllerStatus.Running : Zapret.OurProcessAlive;
    public Task<bool> Healthy() => Task.FromResult(Alive());
    public Task<(long rx, long tx)> Traffic() => Task.FromResult((0L, 0L));
    public async Task<bool> Restart() => _viaService ? Alive() : await Zapret.Start(_strategy);
    public Task Stop()
    {
        if (!_viaService) Zapret.StopOurs();
        return Task.CompletedTask;
    }
}
