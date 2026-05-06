using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Valyze.Mcp.Backend;

/// <summary>
/// Thin HTTP wrapper around the Valyze backend API. Caches a JWT obtained
/// via <c>/auth/dev-login</c> for the lifetime of the process — fine for
/// personal mode, will need a swap when multi-user lands.
///
/// All public methods return raw JSON strings rather than typed records:
/// the MCP layer ships them straight to the model, which prefers a
/// formatted JSON document over a re-serialised projection.
/// </summary>
public sealed class ValyzeApiClient : IDisposable
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<ValyzeApiClient> _logger;
    private string? _accessToken;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public ValyzeApiClient(HttpClient http, ILogger<ValyzeApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> GetPositionsAsync(CancellationToken cancellationToken)
        => await GetJsonAsync("/api/positions/", cancellationToken).ConfigureAwait(false);

    public async Task<string> GetPortfolioAsync(CancellationToken cancellationToken)
        => await GetJsonAsync("/api/portfolio/", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Fetches the full positions view (including per-position trade history)
    /// and reshapes it into a flat list of trades. Useful when the model
    /// wants to reason over execution history without us standing up a
    /// dedicated /api/trades endpoint just for the AI.
    /// </summary>
    public async Task<string> GetTradesAsync(string? symbolFilter, CancellationToken cancellationToken)
    {
        var positionsJson = await GetJsonAsync("/api/positions/", cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(positionsJson);
        var positions = doc.RootElement.GetProperty("positions");

        var rows = new List<object>();
        foreach (var p in positions.EnumerateArray())
        {
            var symbol = p.GetProperty("symbol").GetString() ?? "";
            if (!string.IsNullOrEmpty(symbolFilter)
                && !symbol.Equals(symbolFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;

            if (!p.TryGetProperty("trades", out var tradesEl) || tradesEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var t in tradesEl.EnumerateArray())
            {
                rows.Add(new
                {
                    symbol,
                    name,
                    id = t.GetProperty("id").GetString(),
                    executedAt = t.GetProperty("executedAt").GetString(),
                    side = t.GetProperty("side").GetString(),
                    quantity = t.GetProperty("quantity").GetDecimal(),
                    price = t.GetProperty("price"),
                    fees = t.GetProperty("fees"),
                    brokerKey = t.GetProperty("brokerKey").GetString(),
                    brokerReference = t.TryGetProperty("brokerReference", out var br)
                        && br.ValueKind == JsonValueKind.String ? br.GetString() : null,
                });
            }
        }

        return JsonSerializer.Serialize(new { count = rows.Count, trades = rows }, PrettyJson);
    }

    /// <summary>
    /// GET an arbitrary path on the backend (relative to BaseAddress) and
    /// return the raw JSON body. Used by tools that don't need any
    /// reshape — they can pass the response straight through to the model.
    /// </summary>
    public async Task<string> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token may have expired (12h TTL by default). Drop and retry once.
            _accessToken = null;
            await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
            using var retry = new HttpRequestMessage(HttpMethod.Get, path);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var retryResponse = await _http.SendAsync(retry, cancellationToken).ConfigureAwait(false);
            retryResponse.EnsureSuccessStatusCode();
            return await retryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// POST a JSON body (or no body) and return the response payload as a
    /// JSON string. Empty 204 responses come back as <c>"{}"</c> so tools
    /// can always parse what they get.
    /// </summary>
    public async Task<string> PostJsonAsync(string path, string? body, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return "{}";
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// PATCH a JSON body (or no body) to the given path and return the
    /// response payload as a JSON string. Empty 204 responses come back as
    /// <c>"{}"</c> so tools can always parse what they get.
    /// </summary>
    public async Task<string> PatchJsonAsync(string path, string? body, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return "{}";
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken)) return;
        await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken)) return;
            _logger.LogInformation("Authenticating against {BaseAddress}", _http.BaseAddress);
            using var response = await _http.PostAsync("/auth/dev-login", content: null, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content
                .ReadFromJsonAsync<DevLoginResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                throw new InvalidOperationException("dev-login returned an empty token");
            _accessToken = payload.AccessToken;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private sealed record DevLoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("accountId")] string AccountId,
        [property: JsonPropertyName("email")] string Email);

    public void Dispose() => _authLock.Dispose();
}
