namespace FsocietyNet.Services;

public static class VpnState
{
    private static bool _connected;
    public static bool IsConnected
    {
        get => _connected;
        set { _connected = value; StateChanged?.Invoke(value, ConnectedServerName); }
    }
    public static string? ConnectedServerName { get; set; }
    public static event Action<bool, string?>? StateChanged;
    public static event Action? ConnectBestRequested;
    public static event Action? DisconnectRequested;
    public static void RequestConnectBest() => ConnectBestRequested?.Invoke();
    public static void RequestDisconnect()   => DisconnectRequested?.Invoke();
}
