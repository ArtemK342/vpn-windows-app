namespace FsocietyNet.Models;

public record LoginResponse(string access_token, string token_type);
public record UserResponse(string id, string email, bool is_active, string role = "user");
public record ServerResponse(string id, string name, string country, bool is_active, string? endpoint = null);
public record SubscriptionResponse(bool is_active, string? plan, string? expires_at);
public record VpnConfigResponse(string? config, string? client_ip, string? public_key, string? message);
public record RegisterRequest(string email, string password);
public record ChangePasswordRequest(string old_password, string new_password);
