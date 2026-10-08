using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using AngryMonkey.CloudLogin.V3;

namespace AngryMonkey.CloudLogin;

/// <summary>An admin call the authority refused. <see cref="Status"/> is the HTTP status; the message is safe to show.</summary>
public sealed class CloudLoginAdminException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public bool IsForbidden => Status == HttpStatusCode.Forbidden;
}

/// <summary>
/// Typed access to the CloudLogin admin API (<c>api/v3/admin</c>). It rides the signed-in browser's
/// cookie, so it only works for someone signed in to the authority, and what it may do is decided
/// there from the stored account, not here.
/// </summary>
public sealed class CloudLoginAdminClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Root = "api/v3/admin";

    // ── Overview ─────────────────────────────────────────────────────────────────────────────

    public Task<V3AdminMeResponse> GetMeAsync(CancellationToken ct = default) => Get<V3AdminMeResponse>("me", ct);

    public Task<V3AdminDashboardResponse> GetDashboardAsync(CancellationToken ct = default) => Get<V3AdminDashboardResponse>("dashboard", ct);

    public Task<V3AdminAuditPageResponse> GetAuditAsync(
        string? eventType = null, string? prefix = null, string? clientId = null, Guid? userId = null, string? result = null, int take = 100, CancellationToken ct = default) =>
        Get<V3AdminAuditPageResponse>(Query("audit", ("eventType", eventType), ("prefix", prefix), ("clientId", clientId), ("userId", userId?.ToString()), ("result", result), ("take", take.ToString())), ct);

    // ── Users ────────────────────────────────────────────────────────────────────────────────

    public Task<List<V3AdminUserSummaryResponse>> SearchUsersAsync(string? term, int skip = 0, int take = 25, CancellationToken ct = default) =>
        Get<List<V3AdminUserSummaryResponse>>(Query("users", ("term", term), ("skip", skip.ToString()), ("take", take.ToString())), ct);

    public Task<V3AdminUserDetailResponse> GetUserAsync(Guid userId, CancellationToken ct = default) => Get<V3AdminUserDetailResponse>($"users/{userId}", ct);

    public Task<V3AdminUserSummaryResponse> SetUserDisabledAsync(Guid userId, bool disabled, CancellationToken ct = default) =>
        Send<V3AdminUserSummaryResponse>(HttpMethod.Post, $"users/{userId}/disabled", new V3AdminSetDisabledRequest { Disabled = disabled }, ct);

    public Task<List<V3AdminSessionResponse>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default) =>
        Get<List<V3AdminSessionResponse>>($"users/{userId}/sessions", ct);

    public Task RevokeUserSessionsAsync(Guid userId, CancellationToken ct = default) => Send(HttpMethod.Post, $"users/{userId}/sessions/revoke", null, ct);

    public Task SignOutUserEverywhereAsync(Guid userId, CancellationToken ct = default) => Send(HttpMethod.Post, $"users/{userId}/sign-out-everywhere", null, ct);

    public Task RevokeUserPasskeyAsync(Guid userId, string credentialId, CancellationToken ct = default) =>
        Send(HttpMethod.Delete, $"users/{userId}/passkeys/{Uri.EscapeDataString(credentialId)}", null, ct);

    // ── Applications ─────────────────────────────────────────────────────────────────────────

    public Task<List<V3AdminApplicationResponse>> GetApplicationsAsync(CancellationToken ct = default) => Get<List<V3AdminApplicationResponse>>("applications", ct);

    public Task<V3AdminApplicationResponse> GetApplicationAsync(string clientId, CancellationToken ct = default) =>
        Get<V3AdminApplicationResponse>($"applications/{Uri.EscapeDataString(clientId)}", ct);

    public Task<V3AdminRevokedCountResponse> BlockApplicationAsync(string clientId, string? reason, CancellationToken ct = default) =>
        Send<V3AdminRevokedCountResponse>(HttpMethod.Post, $"applications/{Uri.EscapeDataString(clientId)}/block", new V3AdminBlockApplicationRequest { Reason = reason }, ct);

    public Task UnblockApplicationAsync(string clientId, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"applications/{Uri.EscapeDataString(clientId)}/unblock", null, ct);

    public Task ForgetApplicationAsync(string clientId, CancellationToken ct = default) =>
        Send(HttpMethod.Delete, $"applications/{Uri.EscapeDataString(clientId)}", null, ct);

    public Task<List<V3AdminSessionResponse>> GetApplicationSessionsAsync(string clientId, CancellationToken ct = default) =>
        Get<List<V3AdminSessionResponse>>($"applications/{Uri.EscapeDataString(clientId)}/sessions", ct);

    public Task<V3AdminRevokedCountResponse> RevokeApplicationSessionsAsync(string clientId, CancellationToken ct = default) =>
        Send<V3AdminRevokedCountResponse>(HttpMethod.Post, $"applications/{Uri.EscapeDataString(clientId)}/sessions/revoke", null, ct);

    // ── Secret keys ──────────────────────────────────────────────────────────────────────────

    public Task<List<V3AdminSecretKeyModel>> GetSecretKeysAsync(CancellationToken ct = default) => Get<List<V3AdminSecretKeyModel>>("secret-keys", ct);

    public Task<List<V3AdminSecretKeyModel>> GetExpiringSecretKeysAsync(int withinDays = 30, CancellationToken ct = default) =>
        Get<List<V3AdminSecretKeyModel>>($"secret-keys/expiring?withinDays={withinDays}", ct);

    /// <summary>Creates a key. The returned secret is shown once and cannot be read again.</summary>
    public Task<V3AdminIssuedSecretKeyResponse> CreateSecretKeyAsync(string? label, DateTimeOffset? expiresOn, CancellationToken ct = default) =>
        Send<V3AdminIssuedSecretKeyResponse>(HttpMethod.Post, "secret-keys", new V3AdminCreateSecretKeyRequest { Label = label, ExpiresOn = expiresOn }, ct);

    public Task RevokeSecretKeyAsync(string keyId, string? reason, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"secret-keys/{Uri.EscapeDataString(keyId)}/revoke", new V3AdminRevokeRequest { Reason = reason }, ct);

    // ── Sessions ─────────────────────────────────────────────────────────────────────────────

    public Task<List<V3AdminSessionResponse>> GetSessionsAsync(string? audience = null, Guid? userId = null, int take = 100, CancellationToken ct = default) =>
        Get<List<V3AdminSessionResponse>>(Query("sessions", ("audience", audience), ("userId", userId?.ToString()), ("take", take.ToString())), ct);

    public Task RevokeSessionAsync(string familyId, CancellationToken ct = default) =>
        Send(HttpMethod.Post, $"sessions/{Uri.EscapeDataString(familyId)}/revoke", null, ct);

    // ── Security ─────────────────────────────────────────────────────────────────────────────

    public Task<V3AdminKeysResponse> GetKeysAsync(CancellationToken ct = default) => Get<V3AdminKeysResponse>("keys", ct);

    public Task<V3AdminKeyModel> RotateKeyAsync(CancellationToken ct = default) => Send<V3AdminKeyModel>(HttpMethod.Post, "keys/rotate", null, ct);

    public Task<List<V3AdminProviderResponse>> GetProvidersAsync(CancellationToken ct = default) => Get<List<V3AdminProviderResponse>>("providers", ct);

    public Task<List<V3AdminProviderUserModel>> GetProviderUsersAsync(string code, CancellationToken ct = default) =>
        Get<List<V3AdminProviderUserModel>>($"providers/{Uri.EscapeDataString(code)}/users", ct);

    // ── Administrators ───────────────────────────────────────────────────────────────────────

    public Task<List<V3AdministratorResponse>> GetAdministratorsAsync(CancellationToken ct = default) => Get<List<V3AdministratorResponse>>("administrators", ct);

    public Task<V3AdministratorResponse> SetRolesAsync(Guid userId, IEnumerable<string> roles, CancellationToken ct = default) =>
        Send<V3AdministratorResponse>(HttpMethod.Put, $"administrators/{userId}/roles", new V3AdminSetRolesRequest { Roles = [.. roles] }, ct);

    // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

    private static string Query(string path, params (string Name, string? Value)[] parameters)
    {
        string query = string.Join('&', parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{parameter.Name}={HttpUtility.UrlEncode(parameter.Value)}"));

        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"{Root}/{path}", ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new CloudLoginAdminException(response.StatusCode, "The authority returned an empty reply.");
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendCore(method, path, body, ct);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new CloudLoginAdminException(response.StatusCode, "The authority returned an empty reply.");
    }

    private async Task Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendCore(method, path, body, ct);
    }

    private async Task<HttpResponseMessage> SendCore(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using HttpRequestMessage request = new(method, $"{Root}/{path}");

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response = await http.SendAsync(request, ct);

        try
        {
            await EnsureSuccessAsync(response, ct);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "You are signed out. Sign in again.",
            HttpStatusCode.Forbidden => "Your administrator role does not allow this.",
            HttpStatusCode.NotImplemented => "CloudLogin core storage is not available on this deployment.",
            _ => "The request failed."
        };

        try
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("detail", out JsonElement detail) &&
                detail.ValueKind == JsonValueKind.String &&
                detail.GetString() is { Length: > 0 } text)
                message = text;
        }
        catch (JsonException)
        {
        }

        throw new CloudLoginAdminException(response.StatusCode, message);
    }
}
