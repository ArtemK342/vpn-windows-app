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

    private static bool TunnelServiceExists()
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

    public async Task<bool> ConnectAsync(string config)
    {
        if (!IsWireGuardInstalled)
            throw new Exception("AmneziaWG не установлен.\nСкачайте: github.com/amnezia-vpn/amneziawg-windows-client/releases");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FsocietyNet");
        Directory.CreateDirectory(dir);

        var configPath = Path.Combine(dir, $"{TunnelName}.conf");
        await File.WriteAllTextAsync(configPath, config);

        // Удаляем старый туннель только если он существует
        if (TunnelServiceExists())
        {
            await RunAsync($"/uninstalltunnelservice {TunnelName}");
            await Task.Delay(500);
        }

        return await RunAsync($"/installtunnelservice \"{configPath}\"");
    }

    public async Task DisconnectAsync()
    {
        if (TunnelServiceExists())
            await RunAsync($"/uninstalltunnelservice {TunnelName}");
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
