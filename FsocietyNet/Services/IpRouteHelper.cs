using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FsocietyNet.Services;

/// <summary>
/// Утилиты для управления маршрутами Windows и построения AllowedIPs для WireGuard.
/// Поддерживает ввод IP, CIDR и доменных имён (авторезолв → IP).
/// </summary>
public static class IpRouteHelper
{
    // ──────────────── Пресет RU Essential ────────────────

    public static readonly IReadOnlyList<string> RuEssentialPreset = new[]
    {
        // Госуслуги и государство
        "gosuslugi.ru", "esia.gosuslugi.ru", "nalog.ru", "lkfl2.nalog.ru",
        "mos.ru", "pgu.mos.ru", "government.ru", "kremlin.ru", "duma.gov.ru",
        "minfin.gov.ru", "rosstat.gov.ru", "fssp.gov.ru",
        // Банки
        "sberbank.ru", "sber.ru", "online.sberbank.ru",
        "tinkoff.ru", "alfabank.ru", "vtb.ru", "gazprombank.ru",
        "psbank.ru", "rshb.ru", "open.ru", "mkb.ru", "rosbank.ru",
        "unicreditbank.ru", "otpbank.ru", "pochtabank.ru",
        // Соцсети и почта
        "vk.com", "vk.ru", "m.vk.com", "api.vk.com", "video.vk.com", "music.vk.com",
        "ok.ru", "mail.ru", "e.mail.ru", "cloud.mail.ru",
        "dzen.ru", "rutube.ru",
        // Яндекс
        "yandex.ru", "ya.ru", "yandex.net",
        "mail.yandex.ru", "disk.yandex.ru", "maps.yandex.ru",
        "taxi.yandex.ru", "market.yandex.ru", "music.yandex.ru",
        "passport.yandex.ru", "storage.yandexcloud.net", "s3.yandexcloud.net",
        // Маркетплейсы и сервисы
        "ozon.ru", "wildberries.ru", "wb.ru", "avito.ru", "youla.ru",
        "cdek.ru", "boxberry.ru", "pochta.ru",
        // Доставка еды
        "dodo.ru", "vkusnoitochka.ru", "kfc.ru", "rostics.ru",
        // Операторы
        "mts.ru", "megafon.ru", "beeline.ru", "t2.ru",
        // Разное
        "2gis.ru", "auto.ru", "hh.ru", "pikabu.ru", "habr.com",
        "rbc.ru", "tass.ru", "ria.ru", "lenta.ru", "gazeta.ru",
        "kp.ru", "iz.ru", "vedomosti.ru", "kommersant.ru",
        "kinopoisk.ru", "ivi.ru", "okko.tv",
        "cdn.vk.com", "cdn.mail.ru",
        "spb.ru", "msk.ru", "ekb.ru", "nn.ru", "sochi.ru"
    };

    // ──────────────── AllowedIPs (async — резолвим домены) ────────────────

    /// <summary>
    /// Строит строку AllowedIPs на основе режима туннелирования сайтов.
    /// Домены резолвятся в IP в момент подключения.
    /// Exclude → CIDR-алгебра вычитания из 0.0.0.0/0 (как на Android).
    /// Include → только указанные IP/32.
    /// </summary>
    public static async Task<string> BuildAllowedIPsAsync(AppConfig cfg)
    {
        if (cfg.SiteTunnelMode == TunnelMode.All || cfg.TunnelSites.Count == 0)
            return "0.0.0.0/0";

        var ips = await ResolveEntriesAsync(cfg.TunnelSites);
        if (ips.Count == 0) return "0.0.0.0/0";

        if (cfg.SiteTunnelMode == TunnelMode.Include)
        {
            // Только резолвленные IP идут через VPN
            return string.Join(", ", ips.Select(ip => $"{ip}/32"));
        }
        else // Exclude
        {
            // Весь трафик через VPN, кроме резолвленных IP (CIDR complement)
            var routes = new List<string> { "0.0.0.0/0" };
            foreach (var ip in ips)
                ExcludeIpFromRoutes(routes, ip);
            return string.Join(", ", routes);
        }
    }

    /// <summary>Резолвит список доменов/IP в список уникальных IPv4-адресов.</summary>
    public static async Task<List<string>> ResolveEntriesAsync(IEnumerable<string> entries)
    {
        var result = new List<string>();
        foreach (var entry in entries)
        {
            var raw = entry.Trim().ToLowerInvariant()
                .TrimStart("https://".ToCharArray())
                .TrimStart("http://".ToCharArray())
                .TrimStart("www.".ToCharArray())
                .Split('/')[0].Split('?')[0].Split('#')[0];

            // Уже IP — добавляем напрямую
            if (IPAddress.TryParse(raw, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
            {
                result.Add(raw);
                continue;
            }

            // CIDR — берём только base IP
            if (raw.Contains('/') && IPAddress.TryParse(raw.Split('/')[0], out _))
            {
                result.Add(raw.Split('/')[0]);
                continue;
            }

            // Домен — резолвим
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(raw);
                result.AddRange(
                    addresses
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.ToString()));
            }
            catch { /* не резолвится — пропускаем */ }
        }
        return result.Distinct().ToList();
    }

    // ──────────────── CIDR-алгебра (порт алгоритма из Android) ────────────────

    private static void ExcludeIpFromRoutes(List<string> routes, string ip)
    {
        var targetInt = IpToInt(ip);
        var idx = routes.FindIndex(cidr =>
        {
            var (net, prefix) = ParseCidr(cidr);
            return ContainsIp(net, prefix, targetInt);
        });
        if (idx == -1) return;
        var (netN, prefixN) = ParseCidr(routes[idx]);
        routes.RemoveAt(idx);
        SplitExclude(netN, prefixN, targetInt, routes);
    }

    private static void SplitExclude(int net, int prefix, int exclude, List<string> result)
    {
        if (prefix == 32) return; // /32 — сам исключаемый IP, не добавляем
        var newPrefix = prefix + 1;
        var left  = net;
        var right = net | (1 << (31 - prefix));
        if (ContainsIp(left, newPrefix, exclude))
        {
            result.Add($"{IntToIp(right)}/{newPrefix}");
            SplitExclude(left, newPrefix, exclude, result);
        }
        else
        {
            result.Add($"{IntToIp(left)}/{newPrefix}");
            SplitExclude(right, newPrefix, exclude, result);
        }
    }

    private static bool ContainsIp(int net, int prefix, int ip)
    {
        if (prefix == 0) return true;
        var mask = unchecked((int)(0xFFFFFFFF << (32 - prefix)));
        return (net & mask) == (ip & mask);
    }

    private static (int net, int prefix) ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        return (IpToInt(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 32);
    }

    private static int IpToInt(string ip)
    {
        var p = ip.Split('.');
        return (int.Parse(p[0]) << 24) | (int.Parse(p[1]) << 16) |
               (int.Parse(p[2]) << 8)  | int.Parse(p[3]);
    }

    private static string IntToIp(int i) =>
        $"{(i >> 24) & 0xFF}.{(i >> 16) & 0xFF}.{(i >> 8) & 0xFF}.{i & 0xFF}";

    // ──────────────── App firewall rules ────────────────

    /// <summary>
    /// Применяет правила брандмауэра для выбранных приложений.
    /// Exclude: блокируем приложение от VPN-интерфейса → трафик идёт через физический.
    /// Include: блокируем приложение от всех физических интерфейсов → только VPN.
    /// </summary>
    public static async Task ApplyAppRulesAsync(AppConfig cfg, string tunnelName = "FsocietyNet")
    {
        await RemoveAppRulesAsync(tunnelName);

        if (cfg.AppTunnelMode == TunnelMode.All || cfg.TunnelApps.Count == 0) return;

        if (cfg.AppTunnelMode == TunnelMode.Exclude)
        {
            // Блокируем выбранные приложения от VPN-интерфейса
            foreach (var app in cfg.TunnelApps)
            {
                if (!System.IO.File.Exists(app.ExePath)) continue;
                var safeName = SanitizeName(app.Name);
                await RunNetshAsync(
                    $"advfirewall firewall add rule " +
                    $"name=\"FsocietyNet-app-{safeName}\" " +
                    $"program=\"{app.ExePath}\" " +
                    $"dir=out action=block interface=\"{tunnelName}\" profile=any");
            }
        }
        else if (cfg.AppTunnelMode == TunnelMode.Include)
        {
            // Блокируем ВСЕ приложения от VPN, кроме выбранных
            // (выбранные не получают блокировки — идут через VPN как обычно)
            // Получаем все физические интерфейсы и блокируем все НЕвыбранные приложения
            // через них (сложно) — вместо этого блокируем физику для выбранных приложений
            foreach (var app in cfg.TunnelApps)
            {
                if (!System.IO.File.Exists(app.ExePath)) continue;
                var phyIfaces = GetPhysicalInterfaceNames(tunnelName);
                var safeName  = SanitizeName(app.Name);
                int i = 0;
                foreach (var iface in phyIfaces)
                {
                    await RunNetshAsync(
                        $"advfirewall firewall add rule " +
                        $"name=\"FsocietyNet-app-{safeName}-phy{i++}\" " +
                        $"program=\"{app.ExePath}\" " +
                        $"dir=out action=block interface=\"{iface}\" profile=any");
                }
            }
        }
    }

    /// <summary>Удаляет все app-правила брандмауэра FsocietyNet.</summary>
    public static async Task RemoveAppRulesAsync(string tunnelName = "FsocietyNet")
    {
        // Перечисляем и удаляем все правила с нашим префиксом
        var names = GetOurFirewallRuleNames();
        foreach (var n in names)
            await RunNetshAsync($"advfirewall firewall delete rule name=\"{n}\"");
    }

    // ──────────────── Helpers ────────────────

    public static string? GetDefaultGateway()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up
                     && i.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && !i.Name.Equals("FsocietyNet", StringComparison.OrdinalIgnoreCase))
            .SelectMany(i => i.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?.ToString();
    }

    public static List<string> GetPhysicalInterfaceNames(string excludeName)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up
                     && i.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && !i.Name.Equals(excludeName, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .ToList();
    }

    private static List<string> GetOurFirewallRuleNames()
    {
        var names = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "netsh",
                Arguments              = "advfirewall firewall show rule name=all",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true
            };
            var p = Process.Start(psi);
            if (p == null) return names;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                if (line.TrimStart().StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = line.Split(':', 2).Last().Trim();
                    if (name.StartsWith("FsocietyNet-app-", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }
            }
        }
        catch { }
        return names;
    }

    /// <summary>Преобразует CIDR или IP в нормализованную строку CIDR.</summary>
    public static string? NormalizeCidr(string input)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input)) return null;

        // Уже CIDR
        if (input.Contains('/'))
        {
            var parts = input.Split('/');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out _) &&
                int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= 32)
                return input;
            return null;
        }

        // Просто IP → /32
        if (IPAddress.TryParse(input, out _))
            return $"{input}/32";

        return null;
    }

    private static (string? ip, string mask) CidrToIpMask(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix)) return (null, "");
        var ip   = parts[0];
        var bits = prefix == 0 ? 0u : (0xFFFFFFFF << (32 - prefix)) & 0xFFFFFFFF;
        var mask = $"{(bits >> 24) & 0xFF}.{(bits >> 16) & 0xFF}.{(bits >> 8) & 0xFF}.{bits & 0xFF}";
        return (ip, mask);
    }

    private static string SanitizeName(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray())[..Math.Min(32, name.Length)];

    private static async Task RunRouteAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "route",
                Arguments              = args,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch { }
    }

    private static async Task RunNetshAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "netsh",
                Arguments              = args,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch { }
    }
}
