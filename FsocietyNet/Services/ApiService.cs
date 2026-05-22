using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FsocietyNet.Models;

namespace FsocietyNet.Services;

public class ApiService
{
    private const string BaseUrl = "https://fsociety-vpn.org/api/";

    private static readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static event Action? SessionExpired;

    private static void HandleResponse(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionExpired?.Invoke();
        }
        HandleResponse(resp);
    }

    public async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", email),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("grant_type", "password")
        });
        var response = await _client.PostAsync("login", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(_json)
               ?? throw new Exception("Нет ответа от сервера");
    }

    public async Task<UserResponse> GetMeAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "me");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<UserResponse>(_json)
               ?? throw new Exception("Нет ответа от сервера");
    }

    public async Task<List<ServerResponse>> GetServersAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "servers");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<List<ServerResponse>>(_json)
               ?? [];
    }

    public async Task<SubscriptionResponse?> GetSubscriptionAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "subscription");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) { SessionExpired?.Invoke(); return null; }
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<SubscriptionResponse>(_json);
    }

    public async Task<VpnConfigResponse> GetVpnConfigAsync(string token, string serverId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"vpn/config?server_id={serverId}");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<VpnConfigResponse>(_json)
               ?? throw new Exception("Нет ответа от сервера");
    }

    public async Task RegisterAsync(string email, string password)
    {
        var body = JsonSerializer.Serialize(new RegisterRequest(email, password));
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("register", content);
        HandleResponse(resp);
    }

    // ── Тикеты ──

    public async Task<List<TicketResponse>> GetTicketsAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "tickets");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<List<TicketResponse>>(_json) ?? [];
    }

    public async Task<TicketDetailResponse> GetTicketAsync(string token, string ticketId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"tickets/{ticketId}");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<TicketDetailResponse>(_json)
               ?? throw new Exception("Нет ответа");
    }

    public async Task<TicketResponse> CreateTicketAsync(string token, string subject)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "tickets");
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new TicketCreateRequest(subject)),
            Encoding.UTF8, "application/json");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<TicketResponse>(_json)
               ?? throw new Exception("Нет ответа");
    }

    public async Task<TicketMessageResponse> AddTicketMessageAsync(string token, string ticketId, string message)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"tickets/{ticketId}/messages");
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new TicketMessageRequest(message)),
            Encoding.UTF8, "application/json");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
        return await resp.Content.ReadFromJsonAsync<TicketMessageResponse>(_json)
               ?? throw new Exception("Нет ответа");
    }

    public async Task ChangePasswordAsync(string token, string oldPassword, string newPassword)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "change-password");
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new ChangePasswordRequest(oldPassword, newPassword)),
            Encoding.UTF8, "application/json");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
    }

    public async Task CloseTicketAsync(string token, string ticketId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"tickets/{ticketId}/close");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        HandleResponse(resp);
    }

    public async Task<UsageResponse?> GetUsageAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "vpn/usage");
            req.Headers.Add("Authorization", $"Bearer {token}");
            var resp = await _client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<UsageResponse>(_json);
        }
        catch { return null; }
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken)
    {
        try
        {
            var body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { refresh_token = refreshToken }),
                System.Text.Encoding.UTF8, "application/json");
            var resp = await _client.PostAsync("refresh", body);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<LoginResponse>(_json);
        }
        catch { return null; }
    }
}
