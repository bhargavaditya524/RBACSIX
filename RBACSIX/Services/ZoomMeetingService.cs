using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RBACSIX.Services;

public sealed class ZoomOptions
{
    public string? AccountId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public sealed record ZoomMeeting(string MeetingId, string JoinUrl);

public interface IZoomMeetingService
{
    Task<ZoomMeeting> CreateMeetingAsync(
        string title,
        DateTime startTimeUtc,
        int durationMinutes,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    Task DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default);
}

public sealed class ZoomMeetingService : IZoomMeetingService
{
    private const string OAuthBaseUrl = "https://zoom.us/oauth/token";
    private const string ApiBaseUrl = "https://api.zoom.us/v2/";
    private readonly HttpClient _httpClient;
    private readonly ZoomOptions _options;
    private readonly ILogger<ZoomMeetingService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public ZoomMeetingService(
        HttpClient httpClient,
        IOptions<ZoomOptions> options,
        ILogger<ZoomMeetingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ZoomMeeting> CreateMeetingAsync(
        string title,
        DateTime startTimeUtc,
        int durationMinutes,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiBaseUrl}users/me/meetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            topic = title,
            type = 2,
            start_time = startTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            duration = durationMinutes,
            timezone = timeZoneId
        });

        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorDetail = await GetSafeErrorDetailAsync(response, cancellationToken);
            _logger.LogWarning(
                "Zoom meeting creation failed with status {StatusCode}: {ErrorDetail}",
                (int)response.StatusCode,
                errorDetail);
            throw new ZoomMeetingException(
                $"Zoom could not create the meeting (HTTP {(int)response.StatusCode}). {errorDetail}");
        }

        ZoomMeetingResponse? meetingResponse;
        try
        {
            meetingResponse = await response.Content.ReadFromJsonAsync<ZoomMeetingResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Zoom returned an invalid meeting response.");
            throw new ZoomMeetingException("Zoom returned an invalid meeting response. No class was saved.", exception);
        }
        if (meetingResponse is null ||
            string.IsNullOrWhiteSpace(meetingResponse.JoinUrl) ||
            !IsValidMeetingId(meetingResponse.Id))
        {
            _logger.LogWarning("Zoom returned an incomplete meeting response.");
            throw new ZoomMeetingException("Zoom returned an incomplete meeting response. No class was saved.");
        }

        return new ZoomMeeting(meetingResponse.Id.ToString(), meetingResponse.JoinUrl);
    }

    public async Task DeleteMeetingAsync(string meetingId, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBaseUrl}meetings/{Uri.EscapeDataString(meetingId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Zoom meeting cleanup failed for meeting {MeetingId} with status {StatusCode}.",
                meetingId,
                (int)response.StatusCode);
            throw new ZoomMeetingException(
                $"Zoom could not remove the unlinked meeting (HTTP {(int)response.StatusCode}).");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var accountId = _options.AccountId?.Trim();
        var clientId = _options.ClientId?.Trim();
        var clientSecret = _options.ClientSecret;
        if (string.IsNullOrWhiteSpace(accountId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ZoomMeetingException(
                "Zoom is not configured. Set Zoom:AccountId, Zoom:ClientId, and Zoom:ClientSecret in user-secrets or environment variables.");
        }

        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, OAuthBaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "account_credentials",
                ["account_id"] = accountId
            });
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = await GetSafeErrorDetailAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Zoom OAuth token request failed with status {StatusCode}: {ErrorDetail}",
                    (int)response.StatusCode,
                    errorDetail);
                throw new ZoomMeetingException(
                    $"Zoom authentication failed (HTTP {(int)response.StatusCode}). {errorDetail}");
            }

            ZoomTokenResponse? tokenResponse;
            try
            {
                tokenResponse = await response.Content.ReadFromJsonAsync<ZoomTokenResponse>(
                    cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Zoom OAuth returned an invalid response.");
                throw new ZoomMeetingException("Zoom authentication returned an invalid response.", exception);
            }
            if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _logger.LogWarning("Zoom OAuth returned an incomplete token response.");
                throw new ZoomMeetingException("Zoom authentication returned an invalid response.");
            }

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Unable to reach a Zoom API endpoint.");
            throw new ZoomMeetingException("Unable to reach Zoom. Check the network connection and try again.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "A Zoom API request timed out.");
            throw new ZoomMeetingException("Zoom did not respond in time. Try again.", exception);
        }
    }

    private async Task<string> GetSafeErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "Zoom returned an error without a description.";

            var code = ReadString(root, "code") ?? ReadString(root, "error");
            var reason = ReadString(root, "message") ??
                         ReadString(root, "error_description") ??
                         ReadString(root, "reason");
            var detail = string.Join(": ", new[] { code, reason }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var fieldErrors = errors.EnumerateArray()
                    .Select(error =>
                    {
                        var field = ReadString(error, "field");
                        var message = ReadString(error, "message");
                        return string.Join(": ", new[] { field, message }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                var errorDetails = string.Join("; ", fieldErrors);
                if (!string.IsNullOrWhiteSpace(errorDetails))
                    detail = string.IsNullOrWhiteSpace(detail) ? errorDetails : $"{detail} ({errorDetails})";
            }

            if (string.IsNullOrWhiteSpace(detail))
                return "Zoom did not provide an error description.";

            foreach (var secret in new[] { _options.AccountId, _options.ClientId, _options.ClientSecret, _accessToken })
            {
                if (!string.IsNullOrWhiteSpace(secret))
                    detail = detail.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
            }

            return new string(detail
                .Where(character => !char.IsControl(character))
                .Take(240)
                .ToArray());
        }
        catch (JsonException)
        {
            return "Zoom returned an unreadable error response.";
        }
        catch (IOException)
        {
            return "Zoom's error response could not be read.";
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static bool IsValidMeetingId(JsonElement id)
    {
        return (id.ValueKind is JsonValueKind.Number or JsonValueKind.String) &&
               !string.IsNullOrWhiteSpace(id.ToString());
    }

    private sealed class ZoomTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class ZoomMeetingResponse
    {
        [JsonPropertyName("id")]
        public JsonElement Id { get; set; }

        [JsonPropertyName("join_url")]
        public string? JoinUrl { get; set; }
    }
}

public sealed class ZoomMeetingException : Exception
{
    public ZoomMeetingException(string message) : base(message) { }
    public ZoomMeetingException(string message, Exception innerException) : base(message, innerException) { }
}
