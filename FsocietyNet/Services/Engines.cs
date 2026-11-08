using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FsocietyNet.Services;

/// <summary>A running connection of one protocol family.</summary>
public interface IEngine
{
    string Kind { get; }
    /// <summary>Tunnel/proxy still up. AmneziaWG: service running; sing-box / ciadpi: process alive.</summary>
    bool Alive();
    /// <summary>Link passes traffic (AmneziaWG: fresh handshake). Checked by the watchdog.</summary>
    Task<bool> Healthy();
    Task<(long rx, long tx)> Traffic();
    /// <summary>Restart from the local config (no API call — with a kill switch the API may be unreachable).</summary>
    Task<bool> Restart();
    Task Stop();
}

public class EngineException : Exception
{
    public string Key { get; }
    public EngineException(string key, string? detail = null) : base(detail ?? key) => Key = key;
}

static class Proc
{
    public static async Task<(int code, string output)> Run(string exe, string args, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("start " + exe);
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-1, "timeout"); }
        return (p.ExitCode, (await so) + (await se));
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Leftovers of a crashed run: our sing-box / ciadpi from %ProgramData%\FsocietyNet\bin.</summary>
    public static void KillOurs(string exeName)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)))
        {
            try
            {
                var path = p.MainModule?.FileName ?? "";
                if (path.StartsWith(Paths.BinDir, StringComparison.OrdinalIgnoreCase)) p.Kill(true);
            }
            catch { }
        }
    }
}

public static class Net
{
    /// <summary>IPv4 DNS of the physical network (not our tunnels) — for sites that go «direct».</summary>
    public static string? IspDns()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;
                if (ni.Name.Contains("FsocietyNet", StringComparison.OrdinalIgnoreCase) || ni.Description.Contains("Wintun")
                    || ni.Description.Contains("WireGuard") || ni.Description.Contains("AmneziaWG")) continue;
                var props = ni.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
                var dns = props.DnsAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (dns != null) return dns.ToString();
            }
        }
        catch { }
        return null;
    }

    public static async Task<int> Ping(string host)
    {
        if (string.IsNullOrEmpty(host)) return -1;
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(host, 2500);
            if (r.Status == IPStatus.Success) return (int)Math.Max(1, r.RoundtripTime);
        }
        catch { }
        // ICMP closed → TCP connect time to 443
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var cts = new CancellationTokenSource(2500);
            var sw = Stopwatch.StartNew();
            await s.ConnectAsync(host, 443, cts.Token);
            return (int)Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch { return -1; }
    }

    public static readonly Dictionary<string, string> DnsServers = new()
    {
        ["cloudflare"] = "1.1.1.1", ["google"] = "8.8.8.8", ["quad9"] = "9.9.9.9", ["adguard"] = "94.140.14.14",
    };
    public static string DnsIp => DnsServers.TryGetValue(AppSettings.Current.Dns, out var ip) ? ip : "1.1.1.1";
}

// ═══════════════════════════════ AmneziaWG ═══════════════════════════════

/// <summary>
/// AmneziaWG through amneziawg.exe /installtunnelservice (tunnel service «AmneziaWGTunnel$FsocietyNet», runs as
/// SYSTEM). Status and traffic via the service's UAPI pipe. The config with the private key lives in
/// %ProgramData%\FsocietyNet\run (admin-only).
/// </summary>
public sealed class AwgEngine : IEngine
{
    public const string TunnelName = "FsocietyNet";
    const string ServiceName = "AmneziaWGTunnel$" + TunnelName;
    public string Kind => "wireguard";
    static string ConfPath => Path.Combine(Paths.RunDir, TunnelName + ".conf");

    static string Exe => Binaries.Get(Binaries.AmneziaWG) ?? throw new EngineException("err.no_binary", "amneziawg.exe");

    public static bool ServiceExists()
    {
        try { using var sc = new ServiceController(ServiceName); _ = sc.Status; return true; }
        catch { return false; }
    }

    static bool ServiceRunning()
    {
        try { using var sc = new ServiceController(ServiceName); return sc.Status == ServiceControllerStatus.Running; }
        catch { return false; }
    }

    /// <summary>Server config + our key + DNS / LAN / kill switch / site rules → final config.</summary>
    public static async Task<string> BuildConfig(string raw, string? privateKey)
    {
        var s = AppSettings.Current;
        var cfg = privateKey == null ? raw : raw.Replace("CLIENT_PRIVATE_KEY", privateKey);

        // AllowedIPs. Exactly 0.0.0.0/0 turns on the tunnel service's own «block untunneled traffic» (kill switch);
        // 0.0.0.0/1 + 128.0.0.0/1 routes the same but without the block.
        List<string> allowed;
        string? note = null;
        if (s.SitesActive && s.SitesMode == "include")
        {
            allowed = await Rules.ResolveToCidrs(s.Sites);
            allowed.Add(Net.DnsIp + "/32");   // DNS of the tunnel goes through it too
            note = "include";
        }
        else
        {
            var exclude = new List<string>();
            if (s.AllowLan) exclude.AddRange(Cidr.Private);
            if (s.SitesActive) exclude.AddRange(await Rules.ResolveToCidrs(s.Sites));
            if (exclude.Count == 0)
                allowed = s.KillSwitch ? ["0.0.0.0/0"] : ["0.0.0.0/1", "128.0.0.0/1"];
            else
                allowed = Cidr.Subtract(["0.0.0.0/0"], exclude);
        }
        Log.I($"awg allowed-ips: {allowed.Count} block(s) {note}");

        var lines = cfg.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var key = line.Split('=')[0].Trim();
            if (key.Equals("DNS", StringComparison.OrdinalIgnoreCase)) return $"DNS = {Net.DnsIp}";
            if (key.Equals("AllowedIPs", StringComparison.OrdinalIgnoreCase)) return $"AllowedIPs = {string.Join(", ", allowed)}";
            return line;
        });
        return string.Join("\r\n", lines);
    }

    public async Task Start(string config)
    {
        var exe = Exe;
        Binaries.Get(Binaries.Wintun);   // next to amneziawg.exe (the driver DLL)
        Paths.SecureMachineDirs();
        await File.WriteAllTextAsync(ConfPath, config);
        if (ServiceExists()) { await Proc.Run(exe, $"/uninstalltunnelservice {TunnelName}"); await Task.Delay(400); }
        var (code, output) = await Proc.Run(exe, $"/installtunnelservice \"{ConfPath}\"");
        if (code != 0) { Log.I("awg install failed: " + output); throw new EngineException("err.tunnel_install", output); }
        for (int i = 0; i < 40 && !ServiceRunning(); i++) await Task.Delay(250);
        if (!ServiceRunning()) throw new EngineException("err.tunnel_install");

        // With PersistentKeepalive the handshake starts right away; no handshake in 9 s = the server is not reachable
        for (int i = 0; i < 18; i++)
        {
            await Task.Delay(500);
            var st = await Uapi();
            if (st.handshake > 0) { Log.I("awg handshake ok"); return; }
        }
        await Stop();
        throw new EngineException("home.error_server_unreachable");
    }

    public bool Alive() => ServiceRunning();

    public async Task<bool> Healthy()
    {
        var st = await Uapi();
        if (st.handshake == 0) return false;
        // Keepalive every 25 s renews the handshake every ~2 min; 3.5 min without one = the link is dead
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - st.handshake < 210;
    }

    public async Task<(long rx, long tx)> Traffic() { var st = await Uapi(); return (st.rx, st.tx); }

    public async Task<bool> Restart()
    {
        var restarted = await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
                sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return true;
            }
            catch { return false; }
        });
        if (!restarted)
        {
            try { await Proc.Run(Exe, $"/installtunnelservice \"{ConfPath}\""); } catch { return false; }
        }
        for (int i = 0; i < 16; i++) { await Task.Delay(500); if ((await Uapi()).handshake > 0) return true; }
        return false;
    }

    public async Task Stop()
    {
        if (ServiceExists())
        {
            try { await Proc.Run(Exe, $"/uninstalltunnelservice {TunnelName}"); }
            catch (Exception e) { Log.E("awg uninstall", e); }
        }
        try { File.Delete(ConfPath); } catch { }
    }

    /// <summary>
    /// Tunnel left from a crashed run: stop and delete the service directly — after an app update the old
    /// amneziawg.exe is still running it, so the new one could not be extracted before it is gone.
    /// </summary>
    public static async Task Cleanup()
    {
        if (ServiceExists())
        {
            await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
                catch (Exception e) { Log.E("awg cleanup stop", e); }
            });
            try { await Proc.Run("sc.exe", $"delete \"{ServiceName}\""); } catch (Exception e) { Log.E("awg cleanup delete", e); }
            Log.I("awg: leftover tunnel removed");
        }
        try { File.Delete(ConfPath); } catch { }
    }

    /// <summary>WireGuard UAPI over the service pipe: handshake time and bytes of the (single) peer.</summary>
    static async Task<(long handshake, long rx, long tx)> Uapi()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", @"ProtectedPrefix\Administrators\AmneziaWG\" + TunnelName, PipeDirection.InOut);
            await pipe.ConnectAsync(1500);
            var req = Encoding.ASCII.GetBytes("get=1\n\n");
            await pipe.WriteAsync(req);
            await pipe.FlushAsync();
            using var r = new StreamReader(pipe, Encoding.ASCII);
            long hs = 0, rx = 0, tx = 0;
            string? line;
            using var cts = new CancellationTokenSource(1500);
            while ((line = await r.ReadLineAsync(cts.Token)) != null && line.Length > 0)
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "last_handshake_time_sec": hs = Math.Max(hs, long.Parse(kv[1])); break;
                    case "rx_bytes": rx += long.Parse(kv[1]); break;
                    case "tx_bytes": tx += long.Parse(kv[1]); break;
                }
            }
            return (hs, rx, tx);
        }
        catch { return (0, 0, 0); }
    }
}

// ═══════════════════════════════ sing-box ═══════════════════════════════

/// <summary>
/// VLESS / Trojan / Hysteria2: sing-box.exe with a TUN inbound. The server sends the Android config; here it is
/// brought to the sing-box 1.12+ format (new DNS servers, no legacy «block» outbound) and adapted for Windows
/// (auto_detect_interface — otherwise sing-box's own traffic loops into its TUN; strict_route — no DNS leaks).
/// Traffic and the «does the server pass traffic» check go through the local Clash API.
/// </summary>
public sealed class SingBoxEngine : IEngine
{
    public string Kind { get; }
    Process? _proc;
    int _apiPort;
    string _secret = "";
    static string ConfPath => Path.Combine(Paths.RunDir, "sing-box.json");
    static readonly HttpClient Local = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };

    public SingBoxEngine(string kind) => Kind = kind;

    public static string BuildConfig(string raw, int apiPort, string secret)
    {
        var s = AppSettings.Current;
        var root = JsonNode.Parse(raw)!.AsObject();

        root["log"] = new JsonObject { ["level"] = s.LogLevel == "max" ? "info" : "warn", ["timestamp"] = true };

        // ── Outbounds: legacy block/dns outbounds are gone in 1.13+ (rules use actions) ──
        var outbounds = root["outbounds"]?.AsArray() ?? new JsonArray();
        for (int i = outbounds.Count - 1; i >= 0; i--)
            if (outbounds[i]?["type"]?.GetValue<string>() is "block" or "dns") outbounds.RemoveAt(i);
        if (!outbounds.Any(o => o?["tag"]?.GetValue<string>() == "direct"))
            outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = "direct" });
        foreach (var o in outbounds)
        {
            if (o?["type"]?.GetValue<string>() != "hysteria2") continue;
            if (s.HysteriaMode == "brutal")
            {
                o["down_mbps"] = Math.Max(1, (int)Math.Round(s.HysteriaDownMBs * 8));
                o["up_mbps"] = Math.Max(1, (int)Math.Round(s.HysteriaUpMBs * 8));
            }
            else { o.AsObject().Remove("down_mbps"); o.AsObject().Remove("up_mbps"); }
        }
        root["outbounds"] = outbounds;

        // ── DNS: VPN DNS (detour proxy) = the chosen server over DoH; direct DNS = ISP DNS, else DoH directly ──
        var isp = Net.IspDns();
        var servers = new JsonArray
        {
            new JsonObject { ["type"] = "https", ["tag"] = "dns-vpn", ["server"] = Net.DnsIp, ["detour"] = "proxy" },
            isp != null
                ? new JsonObject { ["type"] = "udp", ["tag"] = "dns-direct", ["server"] = isp }
                : new JsonObject { ["type"] = "https", ["tag"] = "dns-direct", ["server"] = "1.1.1.1" },
        };
        var dns = new JsonObject { ["servers"] = servers, ["strategy"] = "ipv4_only", ["final"] = "dns-vpn" };
        root["dns"] = dns;

        // ── TUN for Windows ──
        root["inbounds"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "tun", ["tag"] = "tun-in", ["interface_name"] = "FsocietyNet",
                ["address"] = new JsonArray("172.19.0.1/30"), ["mtu"] = 9000,
                ["auto_route"] = true, ["strict_route"] = true, ["stack"] = "gvisor",
            }
        };

        // ── Route: sniff + DNS hijack first, then the server's rules (private nets direct), then ours ──
        var route = root["route"]?.AsObject() ?? new JsonObject();
        var oldRules = route["rules"]?.AsArray() ?? new JsonArray();
        var rules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
        };
        foreach (var r in oldRules.ToList())
        {
            var action = r?["action"]?.GetValue<string>();
            if (action is "sniff" or "hijack-dns" || r == null) continue;
            if (r["outbound"]?.GetValue<string>() is "block") { r.AsObject().Remove("outbound"); r["action"] = "reject"; }
            rules.Add(r.DeepClone());
        }
        // (private ranges always go direct by the server's rule, so the LAN option is AmneziaWG-only)
        var final = "proxy";
        var dnsRules = new JsonArray();
        if (s.AppsActive)
        {
            var exclude = s.AppsMode == "exclude";
            var names = new JsonArray(s.Apps.Select(a => (JsonNode)a.Exe).ToArray());
            rules.Add(new JsonObject { ["process_name"] = names, ["outbound"] = exclude ? "direct" : "proxy" });
            if (!exclude) final = "direct";
        }
        if (s.SitesActive)
        {
            var exclude = s.SitesMode == "exclude";
            var matched = exclude ? "direct" : "proxy";
            var domains = s.Sites.Where(e => !Rules.IsIp(e) && !Rules.IsProc(e)).ToArray();
            var cidrs = s.Sites.Where(Rules.IsIp).Select(e => e.Contains('/') ? e : e + "/32").ToArray();
            var procs = s.Sites.Where(Rules.IsProc).Select(Rules.ProcName).ToArray();
            if (procs.Length > 0) rules.Add(new JsonObject { ["process_name"] = Arr(procs), ["outbound"] = matched });
            if (domains.Length > 0)
            {
                rules.Add(new JsonObject { ["domain_suffix"] = Arr(domains), ["outbound"] = matched });
                dnsRules.Add(new JsonObject { ["domain_suffix"] = Arr(domains), ["server"] = exclude ? "dns-direct" : "dns-vpn" });
            }
            if (cidrs.Length > 0) rules.Add(new JsonObject { ["ip_cidr"] = Arr(cidrs), ["outbound"] = matched });
            if (!exclude) { final = "direct"; dns["final"] = "dns-direct"; }
        }
        if (dnsRules.Count > 0) dns["rules"] = dnsRules;
        route["rules"] = rules;
        route["final"] = final;
        route["auto_detect_interface"] = true;
        route["default_domain_resolver"] = "dns-direct";
        root["route"] = route;

        root["experimental"] = new JsonObject
        {
            ["clash_api"] = new JsonObject { ["external_controller"] = $"127.0.0.1:{apiPort}", ["secret"] = secret }
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static JsonArray Arr(IEnumerable<string> v) => new(v.Select(x => (JsonNode)x).ToArray());

    public async Task Start(string serverConfig)
    {
        var exe = Binaries.Get(Binaries.SingBox) ?? throw new EngineException("err.no_binary", "sing-box.exe");
        Paths.SecureMachineDirs();
        _apiPort = Proc.FreePort();
        _secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        await File.WriteAllTextAsync(ConfPath, BuildConfig(serverConfig, _apiPort, _secret));
        if (!await Launch(exe)) throw new EngineException("err.singbox_start");

        // Like Android probeThroughProxy: is the server really passing traffic?
        if (!await Probe())
        {
            await Stop();
            throw new EngineException("home.error_server_unreachable");
        }
    }

    async Task<bool> Launch(string exe)
    {
        Proc.KillOurs("sing-box.exe");
        var psi = new ProcessStartInfo(exe, $"run -c \"{ConfPath}\" --disable-color")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Paths.RunDir,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 30) tail.Dequeue(); }
            Log.D("sing-box: " + e.Data);
        }
        p.OutputDataReceived += OnLine;
        p.ErrorDataReceived += OnLine;
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _proc = p;

        // Ready = Clash API answers (TUN is up by then)
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            if (p.HasExited)
            {
                lock (tail) Log.I($"sing-box exited {p.ExitCode}: " + string.Join(" | ", tail.TakeLast(6)));
                return false;
            }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_apiPort}/version");
                req.Headers.Authorization = new("Bearer", _secret);
                using var resp = await Local.SendAsync(req);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }
        }
        return false;
    }

    async Task<bool> Probe()
    {
        foreach (var url in new[] { "https://www.gstatic.com/generate_204", "https://cp.cloudflare.com/" })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"http://127.0.0.1:{_apiPort}/proxies/proxy/delay?timeout=6000&url={Uri.EscapeDataString(url)}");
                req.Headers.Authorization = new("Bearer", _secret);
                using var resp = await Local.SendAsync(req);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }
        }
        return false;
    }

    public bool Alive() => _proc is { HasExited: false };
    public Task<bool> Healthy() => Task.FromResult(Alive());

    public async Task<(long rx, long tx)> Traffic()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_apiPort}/connections");
            req.Headers.Authorization = new("Bearer", _secret);
            using var resp = await Local.SendAsync(req);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return (doc.RootElement.GetProperty("downloadTotal").GetInt64(), doc.RootElement.GetProperty("uploadTotal").GetInt64());
        }
        catch { return (0, 0); }
    }

    public async Task<bool> Restart()
    {
        var exe = Binaries.Get(Binaries.SingBox);
        if (exe == null || !File.Exists(ConfPath)) return false;
        try { _proc?.Kill(true); } catch { }
        return await Launch(exe) && await Probe();
    }

    public async Task Stop()
    {
        try { if (_proc is { HasExited: false }) { _proc.Kill(true); await _proc.WaitForExitAsync(); } } catch { }
        _proc = null;
        try { File.Delete(ConfPath); } catch { }   // has the UUID / password
    }
}

// ═══════════════════════════════ «Режим РФ» ═══════════════════════════════

/// <summary>
/// «Режим РФ» (bypass): YouTube / Discord through byedpi (ciadpi.exe, local SOCKS5) without our server, as on
/// Android. On Windows the domains are sent to ciadpi by the system proxy (PAC served on 127.0.0.1), not by a TUN:
/// a TUN re-assembles ciadpi's TCP segments and the DPI tricks (split / disorder / fake TTL) are lost.
/// Works for browsers and apps that use the system proxy; the Discord app's voice (UDP) is not covered.
/// </summary>
public sealed class ByeDpiEngine : IEngine
{
    public string Kind => "bypass";
    Process? _proc;
    TcpListener? _pac;
    int _socksPort;
    string[] _args = [];

    // Android built-in strategy (BypassManager.kt, tested 14.10.2026), and the byedpi default for Windows as a fallback
    static readonly string Builtin =
        "-j :162.159.128.0/18 --fake -1 --ttl 4 --disorder 1 --udp-fake 6 " +
        "-A t,s -j :162.159.128.0/18 --fake -1 --ttl 6 --disorder 1 " +
        "-A t,s -j :162.159.128.0/18 --fake -1 --ttl 8 --disorder 1 " +
        "-A t,s -j :162.159.128.0/18 --fake -1 --ttl 12 --disorder 1 " +
        "-A n --tlsrec 3 --split 1 --oob 3 --disorder 5 --udp-fake 6";
    const string WindowsDefault = "--split 1 --disorder 3+s --mod-http=h,d --auto=torst --tlsrec 1+s";

    public static readonly string[] Domains =
    {
        "youtube.com", "youtu.be", "youtube-nocookie.com", "youtubekids.com", "googlevideo.com",
        "ytimg.com", "ggpht.com", "youtubei.googleapis.com", "youtube.googleapis.com",
        "youtubeembeddedplayer.googleapis.com", "yt3.googleusercontent.com", "jnn-pa.googleapis.com",
        "wide-youtube.l.google.com", "youtube-ui.l.google.com", "yt-video-upload.l.google.com", "ytimg.l.google.com",
        "discord.com", "discord.gg", "discord.app", "discord.co", "discord.media", "discord.dev",
        "discord.gift", "discord.gifts", "discord.new", "discord.store", "discord.status",
        "discord.design", "dis.gd", "discordapp.com", "discordapp.net", "discordcdn.com",
        "discord-activities.com", "discordactivities.com", "discordsays.com", "discordstatus.com",
        "discordmerch.com", "discordpartygames.com", "discordsez.com",
        "discord-attachments-uploads-prd.storage.googleapis.com", "hcaptcha.com",
        "cloudflare-ech.com", "encryptedsni.com",
    };

    /// <summary>Admin strategy (/app-config access.bypass_strategy) if it only has safe characters.</summary>
    static IEnumerable<string> Strategies(string? remote)
    {
        var r = remote?.Trim() ?? "";
        if (r.Length > 0 && Regex.IsMatch(r, @"^[A-Za-z0-9 +\-,.:=_/]+$")) yield return r;
        yield return Builtin;
        yield return WindowsDefault;
    }

    public async Task Start(string? remoteStrategy)
    {
        var exe = Binaries.Get(Binaries.CiaDpi) ?? throw new EngineException("err.no_binary", "ciadpi.exe");
        Proc.KillOurs("ciadpi.exe");
        foreach (var strategy in Strategies(remoteStrategy))
        {
            _socksPort = Proc.FreePort();
            _args = new[] { "-i", "127.0.0.1", "-p", _socksPort.ToString() }
                .Concat(strategy.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            if (await Launch(exe)) break;
            Log.I("ciadpi rejected strategy, next");
        }
        if (_proc is not { HasExited: false }) throw new EngineException("bypass.error_start");

        StartPac();
        SystemProxy.SetPac($"http://127.0.0.1:{((IPEndPoint)_pac!.LocalEndpoint).Port}/fsociety.pac");
    }

    async Task<bool> Launch(string exe)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in _args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi);
        if (p == null) return false;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log.D("ciadpi: " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.D("ciadpi: " + e.Data); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        // Bad arguments → it exits at once; listening = the port accepts
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(150);
            if (p.HasExited) return false;
            try
            {
                using var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, _socksPort);
                _proc = p;
                return true;
            }
            catch { }
        }
        try { p.Kill(); } catch { }
        return false;
    }

    string Pac()
    {
        var list = string.Join(",", Domains.Select(d => $"\"{d}\""));
        var to = $"SOCKS5 127.0.0.1:{_socksPort}; SOCKS 127.0.0.1:{_socksPort}; DIRECT";
        return "function FindProxyForURL(url, host) {\n" +
               "  host = host.toLowerCase();\n" +
               $"  var d = [{list}];\n" +
               "  for (var i = 0; i < d.length; i++)\n" +
               $"    if (host == d[i] || dnsDomainIs(host, '.' + d[i])) return '{to}';\n" +
               "  return 'DIRECT';\n}\n";
    }

    void StartPac()
    {
        _pac = new TcpListener(IPAddress.Loopback, 0);
        _pac.Start();
        var listener = _pac;
        _ = Task.Run(async () =>
        {
            var body = Encoding.ASCII.GetBytes(Pac());
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            while (true)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    using (c)
                    {
                        try
                        {
                            var s = c.GetStream();
                            var buf = new byte[2048];
                            using var cts = new CancellationTokenSource(3000);
                            await s.ReadAsync(buf, cts.Token);
                            await s.WriteAsync(head);
                            await s.WriteAsync(body);
                        }
                        catch { }
                    }
                });
            }
        });
    }

    public bool Alive() => _proc is { HasExited: false };
    public Task<bool> Healthy() => Task.FromResult(Alive());
    public Task<(long rx, long tx)> Traffic() => Task.FromResult((0L, 0L));

    public async Task<bool> Restart()
    {
        var exe = Binaries.Get(Binaries.CiaDpi);
        if (exe == null) return false;
        return await Launch(exe);
    }

    public Task Stop()
    {
        SystemProxy.Restore();
        try { _pac?.Stop(); } catch { }
        try { if (_proc is { HasExited: false }) _proc.Kill(); } catch { }
        _proc = null;
        return Task.CompletedTask;
    }
}

/// <summary>Windows (WinINet) proxy settings of the current user: set our PAC, restore the previous values.</summary>
public static class SystemProxy
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    record Backup(int? enable, string? server, string? overrides, string? pac);

    [DllImport("wininet.dll", SetLastError = true)]
    static extern bool InternetSetOption(IntPtr h, int option, IntPtr buffer, int len);

    static void Notify()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);   // INTERNET_OPTION_SETTINGS_CHANGED
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);   // INTERNET_OPTION_REFRESH
    }

    public static void SetPac(string url)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (AppSettings.Current.ProxyBackup == null)
        {
            var b = new Backup(k.GetValue("ProxyEnable") as int?, k.GetValue("ProxyServer") as string,
                k.GetValue("ProxyOverride") as string, k.GetValue("AutoConfigURL") as string);
            AppSettings.Current.ProxyBackup = JsonSerializer.Serialize(b);
            AppSettings.Save();
        }
        k.SetValue("AutoConfigURL", url);
        Notify();
        Log.I("system proxy: PAC set");
    }

    /// <summary>Also called on start: after a crash the PAC of a dead run would break YouTube/Discord.</summary>
    public static void Restore()
    {
        var json = AppSettings.Current.ProxyBackup;
        if (json == null) return;
        try
        {
            var b = JsonSerializer.Deserialize<Backup>(json);
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (b?.pac != null) k.SetValue("AutoConfigURL", b.pac); else k.DeleteValue("AutoConfigURL", false);
            Notify();
            Log.I("system proxy restored");
        }
        catch (Exception e) { Log.E("proxy restore", e); }
        AppSettings.Current.ProxyBackup = null;
        AppSettings.Save();
    }
}
