using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RBACSIX.Services;

namespace RBACSIX.Tests.Services;

public class ZoomMeetingServiceTests
{
    private const string AccountId = "test-account-id";
    private const string ClientId = "test-client-id";
    private const string ClientSecret = "test-client-secret";
    private const string AccessToken = "test-access-token";

    [Fact]
    public async Task CreateMeetingAsync_UsesDocumentedOAuthFormAndMeetingRequest()
    {
        CapturedRequest? tokenRequest = null;
        CapturedRequest? meetingRequest = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://zoom.us/oauth/token")
            {
                tokenRequest = await CaptureAsync(request, cancellationToken);
                return JsonResponse(HttpStatusCode.OK, """
                    {"access_token":"test-access-token","token_type":"bearer","expires_in":3600}
                    """);
            }

            meetingRequest = await CaptureAsync(request, cancellationToken);
            return JsonResponse(HttpStatusCode.Created, """
                {"id":123456789,"join_url":"https://zoom.us/j/123456789"}
                """);
        });
        var service = CreateService(handler);
        var startTime = new DateTime(2027, 2, 3, 10, 15, 0, DateTimeKind.Utc);

        var meeting = await service.CreateMeetingAsync("Algebra class", startTime, 60, "Asia/Kolkata");

        Assert.Equal("123456789", meeting.MeetingId);
        Assert.Equal("https://zoom.us/j/123456789", meeting.JoinUrl);
        Assert.NotNull(tokenRequest);
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Equal("https://zoom.us/oauth/token", tokenRequest.RequestUri);
        Assert.Equal("application/x-www-form-urlencoded", tokenRequest.ContentType);
        var form = tokenRequest.Body;
        Assert.Contains("grant_type=account_credentials", form);
        Assert.Contains($"account_id={AccountId}", form);
        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        Assert.Equal($"Basic {expectedBasic}", tokenRequest.Authorization);

        Assert.NotNull(meetingRequest);
        Assert.Equal(HttpMethod.Post, meetingRequest.Method);
        Assert.Equal("https://api.zoom.us/v2/users/me/meetings", meetingRequest.RequestUri);
        Assert.Equal($"Bearer {AccessToken}", meetingRequest.Authorization);
        using var body = JsonDocument.Parse(meetingRequest.Body);
        Assert.Equal("Algebra class", body.RootElement.GetProperty("topic").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("2027-02-03T10:15:00Z", body.RootElement.GetProperty("start_time").GetString());
        Assert.Equal(60, body.RootElement.GetProperty("duration").GetInt32());
        Assert.Equal("Asia/Kolkata", body.RootElement.GetProperty("timezone").GetString());
        Assert.False(body.RootElement.TryGetProperty("start_url", out _));
    }

    [Fact]
    public async Task CreateMeetingAsync_OnBadRequestIncludesSafeZoomErrorAndRedactsCredentials()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://zoom.us/oauth/token")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, $$"""
                    {"access_token":"{{AccessToken}}","token_type":"bearer","expires_in":3600}
                    """));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, $$"""
                {
                  "code": 300,
                  "message": "Validation Failed.",
                  "errors": [
                    { "field": "topic", "message": "Invalid field. {{ClientSecret}}" }
                  ]
                }
                """));
        });
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<ZoomMeetingException>(() =>
            service.CreateMeetingAsync("Algebra class", DateTime.UtcNow.AddDays(1), 60, "Asia/Kolkata"));

        Assert.Contains("HTTP 400", exception.Message);
        Assert.Contains("300: Validation Failed.", exception.Message);
        Assert.Contains("topic: Invalid field.", exception.Message);
        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.DoesNotContain(AccessToken, exception.Message);
    }

    [Fact]
    public async Task DeleteMeetingAsync_UsesAuthenticatedDeleteEndpoint()
    {
        CapturedRequest? deleteRequest = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://zoom.us/oauth/token")
            {
                return JsonResponse(HttpStatusCode.OK, """
                    {"access_token":"test-access-token","token_type":"bearer","expires_in":3600}
                    """);
            }

            deleteRequest = await CaptureAsync(request, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var service = CreateService(handler);

        await service.DeleteMeetingAsync("123456789");

        Assert.NotNull(deleteRequest);
        Assert.Equal(HttpMethod.Delete, deleteRequest.Method);
        Assert.Equal("https://api.zoom.us/v2/meetings/123456789", deleteRequest.RequestUri);
        Assert.Equal("Bearer test-access-token", deleteRequest.Authorization);
    }

    private static ZoomMeetingService CreateService(HttpMessageHandler handler)
    {
        var options = Options.Create(new ZoomOptions
        {
            AccountId = AccountId,
            ClientId = ClientId,
            ClientSecret = ClientSecret
        });
        return new ZoomMeetingService(
            new HttpClient(handler),
            options,
            NullLogger<ZoomMeetingService>.Instance);
    }

    private static async Task<CapturedRequest> CaptureAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
            return new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string RequestUri,
        string? Authorization,
        string? ContentType,
        string Body);
}
