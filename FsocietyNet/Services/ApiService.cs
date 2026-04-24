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
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserResponse>(_json)
               ?? throw new Exception("Нет ответа от сервера");
    }

    public async Task<List<ServerResponse>> GetServersAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "servers");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<ServerResponse>>(_json)
               ?? [];
    }

    public async Task<SubscriptionResponse?> GetSubscriptionAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "subscription");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<SubscriptionResponse>(_json);
    }

    public async Task<VpnConfigResponse> GetVpnConfigAsync(string token, string serverId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"vpn/config?server_id={serverId}");
        req.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<VpnConfigResponse>(_json)
               ?? throw new Exception("Нет ответа от сервера");
    }

    public async Task RegisterAsync(string email, string password)
    {
        var body = JsonSerializer.Serialize(new RegisterRequest(email, password));
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("register", content);
        resp.EnsureSuccessStatusCode();
    }
}
