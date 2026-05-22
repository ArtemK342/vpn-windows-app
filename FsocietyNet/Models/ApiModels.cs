namespace FsocietyNet.Models;

public record LoginResponse(string access_token, string token_type, string refresh_token = "");
public record UserResponse(string id, string email, bool is_active, string role = "user");
public record ServerResponse(string id, string name, string country, bool is_active, string? endpoint = null, string? status = null, bool allow_auto_connect = true);
public record SubscriptionResponse(bool is_active, string? plan, string? expires_at);
public record VpnConfigResponse(string? config, string? client_ip, string? public_key, string? message);
public record RegisterRequest(string email, string password);
public record ChangePasswordRequest(string old_password, string new_password);
public record UsageResponse(bool is_limited, long bytes_used, long limit_bytes, string? resets_at);

// ── Тикеты ──
public record TicketResponse(string id, string subject, string status, string created_at, string updated_at);
public record TicketDetailResponse(TicketResponse ticket, List<TicketMessageResponse> messages);
public record TicketMessageResponse(string id, string ticket_id, string sender, string message, string created_at);
public record TicketCreateRequest(string subject);
public record TicketMessageRequest(string message);
