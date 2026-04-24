using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace FsocietyNet.Services;

public class VpnService
{
    private const string WireGuardPath = @"C:\Program Files\WireGuard\wireguard.exe";
    private const string TunnelName    = "FsocietyNet";

    public bool IsWireGuardInstalled => File.Exists(WireGuardPath);

    public async Task<bool> ConnectAsync(string config)
    {
        if (!IsWireGuardInstalled)
            throw new Exception("WireGuard не установлен.\nСкачайте с wireguard.com");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FsocietyNet");
        Directory.CreateDirectory(dir);

        var configPath = Path.Combine(dir, $"{TunnelName}.conf");
        await File.WriteAllTextAsync(configPath, config);

        // Удаляем старый туннель если был
        await RunAsync($"/uninstalltunnel {TunnelName}");
        await Task.Delay(800);

        return await RunAsync($"/installtunnel \"{configPath}\"");
    }

    public async Task DisconnectAsync()
    {
        await RunAsync($"/uninstalltunnel {TunnelName}");
    }

    private static async Task<bool> RunAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = WireGuardPath,
                Arguments       = args,
                Verb            = "runas",
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden
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

    public static async Task<int> MeasurePingAsync()
    {
        var samples = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var socket = new Socket(
                    AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync("fsociety-vpn.org", 443);
                sw.Stop();
                samples.Add((int)sw.ElapsedMilliseconds);
            }
            catch { }
            if (i < 2) await Task.Delay(300);
        }
        return samples.Count == 0 ? 999 : (int)samples.Average();
    }
}
