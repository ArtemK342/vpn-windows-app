using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace FsocietyNet.Services;

public record SitePreset(string Id, string TitleKey, string[] Entries);
public record AppPreset(string Name, string[] Exes);

/// <summary>
/// Split tunnelling rules (Rules screen). Entries: domain (with all subdomains), zone («ru»), IPv4 / subnet,
/// «proc:Telegram.exe» (whole program, only from presets). Same normalisation as Android SplitTunnelingManager.
/// </summary>
public static class Rules
{
    const string Proc = "proc:";
    static readonly Regex Ipv4 = new(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(/(\d{1,2}))?$");
    static readonly Regex HostRe = new(@"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$");
    static readonly Regex Zone = new(@"^([a-z]{2,63}|xn--[a-z0-9-]{1,59})$");

    public static bool IsIp(string e) => Ipv4.IsMatch(e);
    public static bool IsProc(string e) => e.StartsWith(Proc);
    public static bool IsZone(string e) => !IsIp(e) && !IsProc(e) && !e.Contains('.');
    public static string ProcName(string e) => e[Proc.Length..];

    /// <summary>User input → rule entry; links, www., port and path are dropped, Cyrillic → punycode. null = invalid.</summary>
    public static string? Normalize(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        s = Regex.Replace(s, @"^[a-z][a-z0-9+.-]*://", "");
        if (!Ipv4.IsMatch(s))
        {
            s = s.Split('/')[0].Split('?')[0].Split('#')[0];
            if (s.Contains('@')) s = s[(s.LastIndexOf('@') + 1)..];
        }
        var m = Ipv4.Match(s);
        if (m.Success)
        {
            var oct = Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
            if (oct.Any(o => o > 255)) return null;
            var prefix = m.Groups[6].Value;
            if (prefix.Length > 0 && int.Parse(prefix) > 32) return null;
            var ip = string.Join(".", oct);
            return prefix.Length == 0 || prefix == "32" ? ip : $"{ip}/{prefix}";
        }
        s = s.Split(':')[0];
        if (s.StartsWith("*.")) s = s[2..];
        s = s.Trim('.');
        if (s.StartsWith("www.")) s = s[4..];
        if (s.Length == 0) return null;
        string ascii;
        try { ascii = new IdnMapping().GetAscii(s).ToLowerInvariant(); } catch { return null; }
        if (!HostRe.IsMatch(ascii)) return null;
        if (!ascii.Contains('.') && !Zone.IsMatch(ascii)) return null;
        return ascii;
    }

    public static string Display(string e)
    {
        if (IsIp(e)) return e;
        if (IsProc(e)) return ProcName(e);
        string u;
        try { u = new IdnMapping().GetUnicode(e); } catch { u = e; }
        return e.Contains('.') ? u : "." + u;
    }

    static readonly string[] MetaCidrs =
    {
        "31.13.24.0/21", "31.13.64.0/18", "45.64.40.0/22", "57.141.0.0/24", "57.141.2.0/23", "57.141.4.0/23",
        "57.141.6.0/24", "57.141.8.0/24", "57.141.10.0/24", "57.141.12.0/23", "57.141.14.0/24", "57.141.16.0/22",
        "57.141.20.0/24", "57.141.22.0/24", "57.141.24.0/24", "57.144.0.0/14", "66.220.144.0/20", "69.63.176.0/20",
        "69.171.224.0/19", "74.119.76.0/22", "102.132.96.0/20", "103.4.96.0/22", "129.134.0.0/17", "157.240.0.0/17",
        "157.240.192.0/18", "163.70.128.0/17", "163.77.132.0/23", "163.77.136.0/23", "163.77.160.0/20",
        "173.252.64.0/18", "179.60.192.0/22", "185.60.216.0/22", "185.89.216.0/22", "204.15.20.0/22",
    };

    // Same sets as Android Presets (SplitTunnelingManager.kt, 18.10.2026); «app:…» packages → Windows programs
    public static readonly SitePreset[] Presets =
    {
        new("ru", "preset.ru_sites", new[]
        {
            "gosuslugi.ru", "nalog.ru", "nalog.gov.ru", "mos.ru", "government.ru", "kremlin.ru", "duma.gov.ru",
            "minfin.gov.ru", "rosstat.gov.ru", "fssp.gov.ru", "pfr.gov.ru", "sfr.gov.ru",
            "sberbank.ru", "sber.ru", "sberbank.com", "tinkoff.ru", "tbank.ru", "alfabank.ru", "vtb.ru",
            "gazprombank.ru", "psbank.ru", "rshb.ru", "open.ru", "mkb.ru", "rosbank.ru", "raiffeisen.ru",
            "otpbank.ru", "pochtabank.ru", "sovcombank.ru", "nspk.ru", "mironline.ru",
            "vk.com", "vk.ru", "userapi.com", "vkuser.net", "ok.ru", "mail.ru", "dzen.ru", "rutube.ru",
            "yandex.ru", "ya.ru", "yandex.net", "yandex.com", "yastatic.net", "yandexcloud.net", "kinopoisk.ru",
            "ozon.ru", "wildberries.ru", "wb.ru", "wbbasket.ru", "avito.ru", "avito.st", "youla.ru",
            "cdek.ru", "boxberry.ru", "pochta.ru", "dodo.ru", "vkusnoitochka.ru", "rostics.ru",
            "mts.ru", "megafon.ru", "beeline.ru", "t2.ru", "rt.ru",
            "2gis.ru", "auto.ru", "hh.ru", "pikabu.ru", "habr.com",
            "rbc.ru", "tass.ru", "ria.ru", "lenta.ru", "gazeta.ru", "kp.ru", "iz.ru", "vedomosti.ru", "kommersant.ru",
            "ivi.ru", "okko.tv", "start.ru", "wink.ru", "premier.one",
        }),
        new("ru_zone", "preset.ru_zone", new[] { "ru", "xn--p1ai", "su" }),
        new("youtube", "preset.youtube", new[]
        {
            "youtube.com", "youtu.be", "youtube-nocookie.com", "youtubekids.com", "googlevideo.com",
            "ytimg.com", "ggpht.com", "youtubei.googleapis.com", "youtube.googleapis.com",
            "youtubeembeddedplayer.googleapis.com", "yt3.googleusercontent.com", "jnn-pa.googleapis.com", "yt.be",
        }),
        new("meta", "preset.meta", new[]
        {
            "instagram.com", "cdninstagram.com", "instagr.am", "ig.me",
            "facebook.com", "fbcdn.net", "fb.com", "fb.me", "facebook.net", "fbsbx.com", "messenger.com", "m.me",
            "threads.net", "threads.com", "meta.com", "meta.ai",
        }.Concat(MetaCidrs).ToArray()),
        new("whatsapp", "preset.whatsapp", new[]
        {
            "whatsapp.com", "whatsapp.net", "wa.me", "proc:WhatsApp.exe", "proc:WhatsApp.Root.exe",
        }.Concat(MetaCidrs).ToArray()),
        new("discord", "preset.discord", new[]
        {
            "discord.com", "discord.gg", "discord.app", "discord.co", "discord.media", "discord.dev",
            "discord.gift", "discord.new", "discordapp.com", "discordapp.net", "discordcdn.com",
            "discord-activities.com", "discordactivities.com", "discordsays.com", "discordstatus.com", "dis.gd",
            "proc:Discord.exe", "proc:DiscordPTB.exe", "proc:DiscordCanary.exe",
        }),
        new("telegram", "preset.telegram", new[]
        {
            "telegram.org", "t.me", "telegram.me", "telesco.pe", "tdesktop.com", "telegra.ph", "graph.org",
            "91.108.4.0/22", "91.108.8.0/22", "91.108.12.0/22", "91.108.16.0/22", "91.108.20.0/22",
            "91.108.56.0/22", "91.105.192.0/23", "149.154.160.0/20", "185.76.151.0/24", "95.161.64.0/20",
            "proc:Telegram.exe", "proc:AyuGram.exe", "proc:Kotatogram.exe",
        }),
        new("twitter", "preset.twitter", new[]
        {
            "x.com", "twitter.com", "twimg.com", "t.co", "twttr.com",
            "64.63.0.0/18", "103.252.112.0/22", "104.244.42.0/24", "104.244.44.0/22", "188.64.224.0/21",
            "192.133.76.0/22", "199.16.156.0/22", "199.59.148.0/22", "199.96.56.0/23", "202.160.128.0/22",
        }),
        new("chatgpt", "preset.chatgpt", new[]
        {
            "openai.com", "chatgpt.com", "chat.com", "oaistatic.com", "oaiusercontent.com", "sora.com",
            "claude.ai", "claude.com", "anthropic.com", "claudeusercontent.com",
            "gemini.google.com", "aistudio.google.com", "generativelanguage.googleapis.com",
            "proc:ChatGPT.exe", "proc:Claude.exe",
        }),
    };

    /// <summary>«Blocked in RF» programs (apps tab, «only these through VPN»): Windows builds of the Android set.</summary>
    public static readonly AppPreset[] BlockedApps =
    {
        new("Telegram", new[] { "Telegram.exe", "AyuGram.exe", "Kotatogram.exe" }),
        new("WhatsApp", new[] { "WhatsApp.exe", "WhatsApp.Root.exe" }),
        new("Viber", new[] { "Viber.exe" }),
        new("Signal", new[] { "Signal.exe" }),
        new("Discord", new[] { "Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe" }),
        new("ChatGPT", new[] { "ChatGPT.exe" }),
        new("Claude", new[] { "Claude.exe" }),
        new("Spotify", new[] { "Spotify.exe" }),
        new("Roblox", new[] { "RobloxPlayerBeta.exe", "RobloxStudioBeta.exe" }),
        new("Netflix", new[] { "Netflix.exe" }),
        new("Proton Mail", new[] { "Proton Mail.exe" }),
        new("Proton Drive", new[] { "ProtonDrive.exe" }),
        new("Tor Browser", new[] { "tor.exe" }),
    };

    // ── Resolving for AmneziaWG (AllowedIPs works with addresses only) ──

    public static async Task<List<string>> ResolveToCidrs(IEnumerable<string> entries)
    {
        var result = new List<string>();
        var tasks = entries.Where(e => !IsProc(e) && !IsZone(e)).Select(async e =>
        {
            if (IsIp(e)) return new[] { e.Contains('/') ? e : e + "/32" };
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var hosts = new[] { e, "www." + e };
                var all = new List<string>();
                foreach (var h in hosts)
                {
                    try
                    {
                        var a = await Dns.GetHostAddressesAsync(h, AddressFamily.InterNetwork, cts.Token);
                        all.AddRange(a.Select(x => x + "/32"));
                    }
                    catch { }
                }
                return all.ToArray();
            }
            catch { return Array.Empty<string>(); }
        });
        foreach (var r in await Task.WhenAll(tasks)) result.AddRange(r);
        return result.Distinct().ToList();
    }
}

/// <summary>IPv4 CIDR set algebra for AllowedIPs (exclude ranges from 0.0.0.0/0, as on Android).</summary>
public static class Cidr
{
    public static readonly string[] Private = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16" };

    readonly record struct Range(uint Start, uint End);

    static Range Parse(string c)
    {
        var p = c.Trim().Split('/');
        var ip = IPAddress.Parse(p[0]).GetAddressBytes();
        uint v = (uint)(ip[0] << 24 | ip[1] << 16 | ip[2] << 8 | ip[3]);
        int len = p.Length > 1 ? int.Parse(p[1]) : 32;
        uint mask = len == 0 ? 0 : uint.MaxValue << (32 - len);
        uint start = v & mask;
        return new(start, start | ~mask);
    }

    /// <summary>include − exclude, as a minimal list of CIDR blocks.</summary>
    public static List<string> Subtract(IEnumerable<string> include, IEnumerable<string> exclude)
    {
        var inc = Merge(include.Select(Parse));
        var exc = Merge(exclude.Select(Parse));
        var result = new List<Range>();
        foreach (var r in inc)
        {
            ulong cur = r.Start;
            foreach (var x in exc)
            {
                if (x.End < cur || x.Start > r.End) continue;
                if (x.Start > cur) result.Add(new((uint)cur, x.Start - 1));
                cur = (ulong)x.End + 1;
                if (cur > r.End) break;
            }
            if (cur <= r.End) result.Add(new((uint)cur, r.End));
        }
        return result.SelectMany(ToCidrs).ToList();
    }

    static List<Range> Merge(IEnumerable<Range> rs)
    {
        var list = rs.OrderBy(r => r.Start).ToList();
        var outp = new List<Range>();
        foreach (var r in list)
        {
            if (outp.Count > 0 && (ulong)outp[^1].End + 1 >= r.Start)
                outp[^1] = new(outp[^1].Start, Math.Max(outp[^1].End, r.End));
            else outp.Add(r);
        }
        return outp;
    }

    static IEnumerable<string> ToCidrs(Range r)
    {
        ulong start = r.Start, end = r.End;
        while (start <= end)
        {
            int len = 32;
            while (len > 0)
            {
                ulong size = 1UL << (33 - len);
                if ((start & (size - 1)) != 0 || start + size - 1 > end) break;
                len--;
            }
            yield return $"{(start >> 24) & 255}.{(start >> 16) & 255}.{(start >> 8) & 255}.{start & 255}/{len}";
            start += 1UL << (32 - len);
        }
    }
}

/// <summary>Programs for the apps tab: Start menu shortcuts (and «choose .exe…»).</summary>
public static class AppScan
{
    static List<TunnelApp>? _cache;

    /// <summary>
    /// Scan on its own STA thread (WScript.Shell is an apartment COM object: from a pool thread every call was
    /// marshalled) and once per run. Never throws: a failure gives an empty list instead of an endless «Loading».
    /// </summary>
    public static Task<List<TunnelApp>> InstalledAsync()
    {
        if (_cache != null) return Task.FromResult(_cache.ToList());
        var tcs = new TaskCompletionSource<List<TunnelApp>>();
        var t = new Thread(() =>
        {
            try { _cache = Installed(); tcs.SetResult(_cache.ToList()); }
            catch (Exception e) { Log.E("app scan", e); tcs.SetResult([]); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    static List<TunnelApp> Installed()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var found = new Dictionary<string, TunnelApp>(StringComparer.OrdinalIgnoreCase);
        dynamic? shell = null;
        try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); } catch { }
        // IgnoreInaccessible: a folder without access used to throw in the middle of the enumeration — the scan
        // died and the Rules screen stayed on «Loading…»
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        var deadline = Environment.TickCount64 + 20_000;
        foreach (var root in roots.Where(Directory.Exists))
        {
            List<string> links;
            try { links = Directory.EnumerateFiles(root, "*.lnk", opts).ToList(); } catch { continue; }
            foreach (var lnk in links)
            {
                if (Environment.TickCount64 > deadline) break;
                try
                {
                    var sc = shell?.CreateShortcut(lnk);
                    string target = sc?.TargetPath ?? "";
                    if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) continue;
                    var exe = Path.GetFileName(target);
                    // Squirrel launchers (Discord, Slack, Teams…): «Update.exe --processStart Discord.exe» — the
                    // program itself is Discord.exe; with Update.exe in the rule its traffic never matched
                    if (exe.Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string args = sc?.Arguments ?? "";
                        var m = System.Text.RegularExpressions.Regex.Match(args, @"--processStart(?:=|\s+)""?([^""\s]+\.exe)""?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        exe = m.Groups[1].Value;
                    }
                    var low = exe.ToLowerInvariant();
                    if (low.Contains("unins") || low.Contains("uninstall") || low.Contains("setup") || low is "explorer.exe" or "cmd.exe")
                        continue;
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (!found.ContainsKey(exe)) found[exe] = new TunnelApp { Name = name, Exe = exe, Path = target };
                }
                catch { }
            }
        }
        return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
