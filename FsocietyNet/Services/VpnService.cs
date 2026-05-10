using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace FsocietyNet.Services;

public class VpnService
{
    private const string TunnelName = "FsocietyNet";

    // Приоритет: вшитый (AppData) → рядом с .exe → системный AmneziaWG → системный WireGuard
    private static string WireGuardPath
    {
        get
        {
            // 1. Извлечённый из embedded resources (основной путь в продакшне)
            var extracted = ResourceExtractor.AmneziawgPath;
            if (File.Exists(extracted)) return extracted;

            // 2. Рядом с .exe (удобно при разработке)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localAwg = Path.Combine(baseDir, "amneziawg.exe");
            if (File.Exists(localAwg)) return localAwg;

            // 3. Системный AmneziaWG (пользователь установил сам)
            var systemAwg = @"C:\Program Files\AmneziaWG\amneziawg.exe";
            if (File.Exists(systemAwg)) return systemAwg;

            // 4. Системный WireGuard (последний fallback)
            var localWg = Path.Combine(baseDir, "wireguard.exe");
            if (File.Exists(localWg)) return localWg;

            return @"C:\Program Files\WireGuard\wireguard.exe";
        }
    }

    public bool IsWireGuardInstalled => File.Exists(WireGuardPath);

    public bool IsAmneziaWG => WireGuardPath.Contains("amneziawg", StringComparison.OrdinalIgnoreCase);

    public static bool TunnelServiceExists()
    {
        // WireGuard/AmneziaWG регистрируют службу с префиксом
        var candidates = new[]
        {
            $"WireGuardTunnel${TunnelName}",
            $"AmneziaWGTunnel${TunnelName}",
            TunnelName
        };
        foreach (var name in candidates)
        {
            try
            {
                using var sc = new ServiceController(name);
                _ = sc.Status; // бросает если не найдена
                return true;
            }
            catch { }
        }
        return false;
    }

    public static string GetWireGuardVersion()
    {
        try
        {
            var path = WireGuardPath;
            if (!File.Exists(path)) return "не установлен";
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion ?? info.FileVersion ?? "неизвестна";
        }
        catch { return "неизвестна"; }
    }

    /// <summary>
    /// Подключается с применением настроек туннелирования из AppConfig.
    /// rawConfig — оригинальный конфиг с сервера; AllowedIPs будет перезаписан.
    /// </summary>
    public async Task<bool> ConnectAsync(string rawConfig)
    {
        if (!IsWireGuardInstalled)
            throw new Exception("AmneziaWG не установлен.\nСкачайте: github.com/amnezia-vpn/amneziawg-windows-client/releases");

        var cfg = AppConfig.Load();

        // Перестраиваем AllowedIPs по настройкам сплит-туннеля
        // (резолвим домены → CIDR complement или include-список)
        var allowedIPs = await IpRouteHelper.BuildAllowedIPsAsync(cfg);
        var config = RewriteAllowedIPs(rawConfig, allowedIPs);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FsocietyNet");
        Directory.CreateDirectory(dir);

        var configPath = Path.Combine(dir, $"{TunnelName}.conf");
        await File.WriteAllTextAsync(configPath, config);

        if (TunnelServiceExists())
        {
            await RunAsync($"/uninstalltunnelservice {TunnelName}");
            await Task.Delay(500);
        }

        var ok = await RunAsync($"/installtunnelservice \"{configPath}\"");

        if (ok)
        {
            // Firewall-правила для приложений
            await IpRouteHelper.ApplyAppRulesAsync(cfg, TunnelName);

            // Kill Switch
            if (cfg.KillSwitch)
                await EnableKillSwitchAsync();
        }

        return ok;
    }

    public async Task DisconnectAsync()
    {
        if (TunnelServiceExists())
            await RunAsync($"/uninstalltunnelservice {TunnelName}");

        // Снимаем app-правила брандмауэра
        await IpRouteHelper.RemoveAppRulesAsync(TunnelName);

        // Kill Switch: правило остаётся — трафик заблокирован пока VPN не включён.
        // Снимается только вручную (пользователь отключает KS или переподключается).
    }

    private static async Task<bool> RunAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = WireGuardPath,
                Arguments              = args,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ───────────────────── Helpers ─────────────────────

    /// <summary>Заменяет строку AllowedIPs в WireGuard-конфиге.</summary>
    private static string RewriteAllowedIPs(string config, string allowedIPs)
    {
        var lines = config.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase))
                lines[i] = $"AllowedIPs = {allowedIPs}";
        }
        return string.Join('\n', lines);
    }

    // ───────────────────── Kill Switch ─────────────────────

    private const string KsRuleName = "FsocietyNet-KillSwitch";

    /// <summary>
    /// Включает Kill Switch: добавляет правило Windows Firewall, блокирующее весь
    /// исходящий трафик. WireGuard/AmneziaWG использует WFP на уровне ниже Windows
    /// Firewall, поэтому трафик через VPN-туннель остаётся доступным.
    /// </summary>
    public static async Task EnableKillSwitchAsync()
    {
        // Сначала удаляем старое правило (если есть), чтобы не было дублей
        await RunNetshAsync($"advfirewall firewall delete rule name=\"{KsRuleName}\"");
        await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{KsRuleName}\" " +
            "dir=out action=block profile=any " +
            "description=\"fsociety VPN Kill Switch — удалится автоматически\"");
    }

    /// <summary>Выключает Kill Switch — удаляет правило блокировки.</summary>
    public static async Task DisableKillSwitchAsync()
    {
        await RunNetshAsync($"advfirewall firewall delete rule name=\"{KsRuleName}\"");
    }

    /// <summary>Проверяет, активно ли правило Kill Switch в Firewall.</summary>
    public static bool IsKillSwitchRuleActive()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "netsh",
                Arguments              = $"advfirewall firewall show rule name=\"{KsRuleName}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
            };
            var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Contains(KsRuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
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

    /// <summary>TCP пинг — fallback когда ICMP заблокирован.</summary>
    public static async Task<int> MeasureTcpAsync(string host, int port)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            await socket.ConnectAsync(host, port);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch { return 999; }
    }

    /// <summary>ICMP пинг к конкретному хосту (IP из endpoint сервера).</summary>
    public static async Task<int> MeasurePingAsync(string host)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                return (int)reply.RoundtripTime;
            return 999;
        }
        catch
        {
            return 999;
        }
    }
}
