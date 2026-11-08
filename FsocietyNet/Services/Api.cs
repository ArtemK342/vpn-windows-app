using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FsocietyNet.Services;

// ── Models (same fields as the Android app, ApiService.kt) ──

public class LoginResponse { public string access_token { get; set; } = ""; public string refresh_token { get; set; } = ""; }

public class UserResponse
{
    public string id { get; set; } = "";
    /// <summary>Anonymous accounts: the server puts the code «XXXX-XXXX-XXXX-XXXX» here.</summary>
    public string? email { get; set; }
    public bool is_active { get; set; } = true;
    public string role { get; set; } = "user";
    public string account_type { get; set; } = "email";
}

public class Localized
{
    public string? code { get; set; }
    public string? message { get; set; }
    public string? message_en { get; set; }
    public string? Text() { var t = Loc.En ? message_en : message; return string.IsNullOrWhiteSpace(t) ? null : t; }
}

public class ServerResponse
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? name_en { get; set; }
    public string country { get; set; } = "";
    public bool is_active { get; set; }
    public string? endpoint { get; set; }
    public bool allow_auto_connect { get; set; } = true;
    public string server_type { get; set; } = "wireguard";   // wireguard | vless | hysteria | trojan | openvpn_cloak | bypass
    public string status { get; set; } = "active";
    public string? status_message { get; set; }
    public string? status_message_extra { get; set; }
    public bool connectable { get; set; }
    public bool subscription_required { get; set; }
    public Localized? access_block { get; set; }
    public bool logs_enabled { get; set; }

    public string DisplayName => Loc.En && !string.IsNullOrWhiteSpace(name_en) ? name_en! : name;
    public bool UsesSingbox => server_type is "vless" or "hysteria" or "trojan";
    public bool IsBypass => server_type == "bypass";
    /// <summary>OpenVPN + Cloak needs openvpn.exe + ck-client + a TAP/DCO driver — not in the Windows app yet.</summary>
    public bool SupportedOnWindows => server_type is "wireguard" or "vless" or "hysteria" or "trojan" or "bypass";
    public bool IsUnavailable => status is "down" or "coming_soon";
    public bool HasWarning => status is "degraded" or "attacked";
    public bool CanConnect => connectable && !subscription_required && access_block == null && SupportedOnWindows;
    public string Host => (endpoint ?? "").Split(':')[0];
}

public class SubscriptionResponse { public bool is_active { get; set; } public string? plan { get; set; } public string? expires_at { get; set; } }
public class UsageResponse { public bool is_limited { get; set; } public long bytes_used { get; set; } public long limit_bytes { get; set; } public string? resets_at { get; set; } }
public class MaintenanceStatus : Localized { public bool enabled { get; set; } }
public class AccessStatus
{
    public MaintenanceStatus? maintenance { get; set; }
    public Dictionary<string, bool>? protocols { get; set; }
    public Dictionary<string, bool>? registration { get; set; }
    public string? bypass_strategy { get; set; }
}
public class AppConfigResponse { public int android_min_version { get; set; } public string update_url { get; set; } = ""; public AccessStatus? access { get; set; } }
public class AccessCheck : Localized { public bool allowed { get; set; } = true; }
public class VpnConfigResponse { public string? config { get; set; } public string? client_ip { get; set; } public string? message { get; set; } public bool client_key { get; set; } }
public class SingboxConfigResponse { public string config { get; set; } = ""; public string server_type { get; set; } = ""; }

public class NewsItem
{
    public string id { get; set; } = "";
    public string title { get; set; } = "";
    public string body { get; set; } = "";
    public string? title_en { get; set; }
    public string? body_en { get; set; }
    public string? image_id { get; set; }
    public string? image_ext { get; set; }
    public string? published_at { get; set; }
    public string Title => Loc.En && !string.IsNullOrWhiteSpace(title_en) ? title_en! : title;
    public string Body => Loc.En && !string.IsNullOrWhiteSpace(body_en) ? body_en! : body;
}
public class NewsPage { public List<NewsItem> items { get; set; } = []; public int total { get; set; } public bool has_more { get; set; } }

public class TicketResponse
{
    public string id { get; set; } = "";
    public string subject { get; set; } = "";
    public string status { get; set; } = "open";
    public string created_at { get; set; } = "";
    public string updated_at { get; set; } = "";
    public int unread { get; set; }
}
public class TicketMessage { public string id { get; set; } = ""; public string sender { get; set; } = ""; public string message { get; set; } = ""; public string created_at { get; set; } = ""; }
public class TicketDetail { public TicketResponse ticket { get; set; } = new(); public List<TicketMessage> messages { get; set; } = []; }
public class GenerateNumericResponse { public string code { get; set; } = ""; public string access_token { get; set; } = ""; public string refresh_token { get; set; } = ""; }

public class ExternalServer { public string id { get; set; } = ""; public string name { get; set; } = ""; public string? name_en { get; set; } public string? country { get; set; } }
public class ExternalKey { public string id { get; set; } = ""; public string server_id { get; set; } = ""; public string? client_ip { get; set; } public string? created_at { get; set; } }
public class ExternalSlots { public int used { get; set; } public int max { get; set; } }
public class ExternalOverview
{
    public bool entitled { get; set; }
    public ExternalSlots slots { get; set; } = new();
    public List<ExternalServer> awg_servers { get; set; } = [];
    public List<ExternalServer> vless_servers { get; set; } = [];
    public List<ExternalKey> keys { get; set; } = [];
    public string? sub_url { get; set; }
    public Dictionary<string, string> vless_lines { get; set; } = [];
    public bool IsAwg(string id) => awg_servers.Any(s => s.id == id);
    public bool IsVless(string id) => vless_servers.Any(s => s.id == id);
}
public class ExternalAwgResponse { public string id { get; set; } = ""; public string amnezia_key { get; set; } = ""; public string config { get; set; } = ""; public string filename { get; set; } = ""; }
public class ExternalSubResponse { public string url { get; set; } = ""; }

public class ApiException : Exception
{
    public int Status { get; }
    public string? Detail { get; }
    public bool Network => Status == 0;
    public ApiException(int status, string? detail, Exception? inner = null) : base(detail ?? $"HTTP {status}", inner)
    { Status = status; Detail = detail; }

    /// <summary>Fixed localised texts by status (Android AppErrors.kt), server text only where it is meant for users.</summary>
    public string UserMessage() => Status switch
    {
        0 => Loc.Get(InnerException is TimeoutException || InnerException?.InnerException is TimeoutException
            ? "err.timeout" : "err.network"),
        401 => Loc.Get("err.session"),
        403 => Loc.En || string.IsNullOrWhiteSpace(Detail) ? Loc.Get("err.forbidden") : Detail!,
        404 => Loc.Get("err.not_found"),
        426 => Loc.Get("err.update"),
        429 => Loc.Get("err.rate_limit"),
        503 => Loc.Get("err.unavailable"),
        >= 500 => Loc.Get("err.server"),
        _ => !Loc.En && !string.IsNullOrWhiteSpace(Detail) ? Detail! : Loc.Get("err.unknown")
    };
}

public static class Errors
{
    public static string Text(Exception e) => e is ApiException a ? a.UserMessage()
        : e is TimeoutException or TaskCanceledException ? Loc.Get("err.timeout")
        : e is HttpRequestException ? Loc.Get("err.network")
        : Loc.Get("err.unknown");
}

/// <summary>
/// fsociety API. Base fsociety-vpn.org/api (Cloudflare); on a network failure the request is repeated directly
/// on api.fsociety-vpn.org (Cloudflare is often cut in RF). /api/vpn/* (configs with keys) and anonymous
/// sessions go direct first, so Cloudflare never sees them — as on Android (ApiService.kt, ApiClient).
/// </summary>
public static class Api
{
    public const string Site = "https://fsociety-vpn.org";
    const string BaseUrl = Site + "/api/";
    public const string DirectHost = "api.fsociety-vpn.org";

    /// <summary>Preview build / `-render`: fixtures from Demo.cs, no network.</summary>
    public static bool DemoMode;

    static readonly HttpClient Http = new(new FallbackHandler(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8),
        // Short-lived connections: they hang after the VPN goes up or down (OkHttp pool is off on Android too)
        PooledConnectionLifetime = TimeSpan.FromSeconds(20),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(4),
        AutomaticDecompression = DecompressionMethods.All,
    }))
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = HttpVersion.Version11,
    };

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    static Api()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"FsocietyNet-Windows/{App.Version}");
    }

    static readonly SemaphoreSlim RefreshGate = new(1, 1);

    static async Task<bool> RefreshTokens(string failedAccess)
    {
        await RefreshGate.WaitAsync();
        try
        {
            if (Session.Access != failedAccess && Session.SignedIn) return true;   // someone else refreshed already
            if (string.IsNullOrEmpty(Session.Refresh)) return false;
            using var req = new HttpRequestMessage(HttpMethod.Post, "refresh")
            {
                Content = JsonContent(new { refresh_token = Session.Refresh })
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return false;
            var r = await resp.Content.ReadFromJsonSafe<LoginResponse>();
            if (r == null || r.access_token.Length == 0) return false;
            Session.Set(r.access_token, r.refresh_token);
            return true;
        }
        catch (Exception e) { Log.E("refresh", e); return false; }
        finally { RefreshGate.Release(); }
    }

    static async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> make, bool auth = true, CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        var used = Session.Access;
        try
        {
            var req = make();
            if (auth) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", used);
            resp = await Http.SendAsync(req, ct);
            if (auth && resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                if (!await RefreshTokens(used)) { Session.Expire(); throw new ApiException(401, null); }
                var again = make();
                again.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Access);
                resp = await Http.SendAsync(again, ct);
                if (resp.StatusCode == HttpStatusCode.Unauthorized) { Session.Expire(); throw new ApiException(401, null); }
            }
        }
        catch (ApiException) { throw; }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested) { throw new ApiException(0, null, new TimeoutException(e.Message, e)); }
        catch (HttpRequestException e) { throw new ApiException(0, null, e); }

        if (!resp.IsSuccessStatusCode)
        {
            string? detail = null;
            try
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("detail", out var d))
                    detail = d.ValueKind == JsonValueKind.String ? d.GetString()
                        : d.ValueKind == JsonValueKind.Object && d.TryGetProperty("message", out var m) ? m.GetString() : d.ToString();
            }
            catch { }
            var code = (int)resp.StatusCode;
            resp.Dispose();
            Log.I($"api {code}: {detail}");
            throw new ApiException(code, detail);
        }
        return resp;
    }

    static async Task<T> Get<T>(string path, bool auth = true) where T : new()
    {
        using var resp = await Send(() => new HttpRequestMessage(HttpMethod.Get, path), auth);
        return await resp.Content.ReadFromJsonSafe<T>() ?? new T();
    }

    static async Task<T> Post<T>(string path, object? body = null, bool auth = true, HttpMethod? method = null) where T : new()
    {
        using var resp = await Send(() => new HttpRequestMessage(method ?? HttpMethod.Post, path)
        {
            Content = body == null ? null : JsonContent(body)
        }, auth);
        return await resp.Content.ReadFromJsonSafe<T>() ?? new T();
    }

    static async Task Call(HttpMethod method, string path, object? body = null)
    {
        using var _ = await Send(() => new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent(body) });
    }

    static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    static async Task<T?> ReadFromJsonSafe<T>(this HttpContent c)
    {
        var s = await c.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(s) ? default : JsonSerializer.Deserialize<T>(s, Json);
    }

    static string Q(string v) => Uri.EscapeDataString(v);

    // ── Auth ──
    public static async Task<LoginResponse> Login(string username, string password)
    {
        if (DemoMode) return Demo.Login();
        using var resp = await Send(() => new HttpRequestMessage(HttpMethod.Post, "login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username, ["password"] = password, ["grant_type"] = "password"
            })
        }, auth: false);
        return await resp.Content.ReadFromJsonSafe<LoginResponse>() ?? new();
    }
    public static Task<LoginResponse> LoginNumeric(string code) =>
        DemoMode ? Task.FromResult(Demo.Login()) : Post<LoginResponse>("login-numeric", new { code }, auth: false);
    public static Task<GenerateNumericResponse> GenerateNumeric() =>
        DemoMode ? Task.FromResult(Demo.Numeric()) : Post<GenerateNumericResponse>("generate-numeric-account", null, auth: false);
    public static Task Register(string email, string password) =>
        DemoMode ? Task.CompletedTask : Post<UserResponse>("register", new { email, password }, auth: false);
    public static Task ForgotPassword(string email) =>
        DemoMode ? Task.CompletedTask : Post<Dictionary<string, object>>("forgot-password", new { email }, auth: false);

    // ── Account ──
    public static async Task<UserResponse> Me()
    {
        var me = DemoMode ? Demo.Me() : await Get<UserResponse>("me");
        Session.SetMe(me);
        return me;
    }
    public static Task DeleteMe() => DemoMode ? Task.CompletedTask : Call(HttpMethod.Delete, "me");
    public static Task ChangePassword(string oldPassword, string newPassword) =>
        DemoMode ? Task.CompletedTask : Call(HttpMethod.Post, "change-password", new { old_password = oldPassword, new_password = newPassword });
    public static async Task<SubscriptionResponse?> Subscription()
    {
        if (DemoMode) return Demo.Subscription();
        try { return await Get<SubscriptionResponse>("subscription"); }
        catch (ApiException e) when (e.Status == 404) { return null; }
    }
    public static Task<UsageResponse> Usage() => DemoMode ? Task.FromResult(Demo.Usage()) : Get<UsageResponse>("vpn/usage");

    // ── Servers / VPN ──
    public static Task<List<ServerResponse>> Servers() => DemoMode ? Task.FromResult(Demo.Servers()) : Get<List<ServerResponse>>("servers");
    public static Task<AppConfigResponse> AppConfig() => DemoMode ? Task.FromResult(Demo.AppConfig()) : Get<AppConfigResponse>("app-config", auth: false);
    public static Task<AccessCheck> Access(string serverType) =>
        DemoMode ? Task.FromResult(new AccessCheck()) : Get<AccessCheck>($"vpn/access?server_type={Q(serverType)}");
    public static Task<VpnConfigResponse> WireGuardConfig(string serverId, string publicKey) =>
        Post<VpnConfigResponse>($"vpn/config?server_id={Q(serverId)}&public_key={Q(publicKey)}");
    public static Task<SingboxConfigResponse> SingboxConfig(string serverType, string serverId) =>
        Get<SingboxConfigResponse>(serverType switch
        {
            "hysteria" => "vpn/hysteria-config",
            "trojan" => "vpn/trojan-config",
            _ => "vpn/vless-config"
        } + $"?server_id={Q(serverId)}");

    // ── News ──
    public static Task<NewsPage> News(int limit, int offset) =>
        DemoMode ? Task.FromResult(Demo.News()) : Get<NewsPage>($"news?limit={limit}&offset={offset}", auth: false);
    public static async Task<byte[]> NewsImage(string id, string? ext)
    {
        if (DemoMode) return [];
        using var resp = await Send(() => new HttpRequestMessage(HttpMethod.Get,
            $"news/images/{id}{(string.IsNullOrWhiteSpace(ext) ? "" : "." + ext)}"), auth: false);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── Support ──
    public static Task<List<TicketResponse>> Tickets() => DemoMode ? Task.FromResult(Demo.Tickets()) : Get<List<TicketResponse>>("tickets");
    public static Task<TicketDetail> Ticket(string id) => DemoMode ? Task.FromResult(Demo.Ticket(id)) : Get<TicketDetail>($"tickets/{id}");
    public static Task<TicketResponse> CreateTicket(string subject) =>
        DemoMode ? Task.FromResult(new TicketResponse { id = "new", subject = subject }) : Post<TicketResponse>("tickets", new { subject });
    public static Task<TicketMessage> SendMessage(string id, string message) =>
        DemoMode ? Task.FromResult(new TicketMessage { sender = "user", message = message, created_at = DateTime.UtcNow.ToString("o") })
                 : Post<TicketMessage>($"tickets/{id}/messages", new { message });
    public static Task CloseTicket(string id) => DemoMode ? Task.CompletedTask : Call(HttpMethod.Patch, $"tickets/{id}/close");
    public static Task DeleteTicket(string id) => DemoMode ? Task.CompletedTask : Call(HttpMethod.Delete, $"tickets/{id}");

    // ── For other apps (AmneziaVPN, Happ…) ──
    public static Task<ExternalOverview> External() => DemoMode ? Task.FromResult(Demo.External()) : Get<ExternalOverview>("vpn/external");
    public static Task<ExternalAwgResponse> CreateExternalAwg(string serverId) => Post<ExternalAwgResponse>($"vpn/external/awg?server_id={Q(serverId)}");
    public static Task<ExternalSubResponse> CreateExternalSub() => Post<ExternalSubResponse>("vpn/external/sub");
}

/// <summary>Cloudflare ↔ direct fallback with the same «safe to repeat» rules as the Android client.</summary>
sealed class FallbackHandler : DelegatingHandler
{
    static long _directUntil, _directDownUntil;
    const long StickyMs = 10 * 60_000;
    static readonly HashSet<string> AnonAuthPaths = ["/api/login-numeric", "/api/generate-numeric-account"];

    public FallbackHandler(HttpMessageHandler inner) : base(inner) { }

    static long Now => Environment.TickCount64;

    static bool BeforeSend(Exception e) =>
        e is HttpRequestException h && (h.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError || h.InnerException is SocketException);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (req.RequestUri!.Host == Api.DirectHost) return await Attempt(req, ct, 25);
        var path = req.RequestUri.AbsolutePath;
        var prefersDirect = path.StartsWith("/api/vpn/") || AnonAuthPaths.Contains(path) || Session.IsAnonymous;
        var idempotent = req.Method == HttpMethod.Get || req.Method == HttpMethod.Head;
        var body = req.Content == null ? null : await req.Content.ReadAsByteArrayAsync(ct);
        var directFirst = Now < Interlocked.Read(ref _directUntil) || (prefersDirect && Now >= Interlocked.Read(ref _directDownUntil));

        if (directFirst)
        {
            try { return await Attempt(Clone(req, body, Api.DirectHost), ct, 15); }
            catch (Exception e) when (!ct.IsCancellationRequested && (idempotent || BeforeSend(e)))
            {
                Log.I($"{path} direct failed ({e.GetType().Name}), via Cloudflare");
                Interlocked.Exchange(ref _directUntil, 0);
                Interlocked.Exchange(ref _directDownUntil, Now + StickyMs);
                return await Attempt(Clone(req, body, null), ct, 25);
            }
        }
        try { return await Attempt(Clone(req, body, null), ct, 15); }
        catch (Exception e) when (!ct.IsCancellationRequested && (idempotent || BeforeSend(e)))
        {
            Log.I($"{path} via Cloudflare failed ({e.GetType().Name}), direct");
            Interlocked.Exchange(ref _directUntil, Now + StickyMs);
            return await Attempt(Clone(req, body, Api.DirectHost), ct, 25);
        }
    }

    /// <summary>One try with its own timeout; the body is read here too, so a stall while reading also falls back.</summary>
    async Task<HttpResponseMessage> Attempt(HttpRequestMessage req, CancellationToken ct, int seconds)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            var resp = await base.SendAsync(req, cts.Token);
            await resp.Content.LoadIntoBufferAsync();
            return resp;
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            // Our timer fired: the request may have reached the server (not safe to repeat a POST).
            // Otherwise it is the handler's connect timeout: nothing was sent.
            var sent = cts.IsCancellationRequested;
            throw new HttpRequestException(sent ? HttpRequestError.Unknown : HttpRequestError.ConnectionError,
                "timeout", new TimeoutException(e.Message));
        }
    }

    static HttpRequestMessage Clone(HttpRequestMessage src, byte[]? body, string? host)
    {
        var uri = host == null ? src.RequestUri! : new UriBuilder(src.RequestUri!) { Host = host }.Uri;
        var r = new HttpRequestMessage(src.Method, uri) { Version = src.Version };
        foreach (var h in src.Headers) r.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (body != null)
        {
            r.Content = new ByteArrayContent(body);
            foreach (var h in src.Content!.Headers) r.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return r;
    }
}
