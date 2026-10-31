using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RBACSIX.Services;

public sealed class CoachingAiProvider(
    HttpClient httpClient,
    IOptions<CoachingAssistantOptions> options)
{
    private readonly CoachingAssistantOptions _options = options.Value;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];

        using var responseJson = await SendAsync(
            _options.EmbeddingApiUrl,
            new
            {
                model = _options.EmbeddingModel,
                input = texts
            },
            cancellationToken);

        if (!responseJson.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The embedding provider returned an invalid response.");

        var items = data
            .EnumerateArray()
            .Select(item =>
            {
                if (!item.TryGetProperty("index", out var index) ||
                    !index.TryGetInt32(out var itemIndex) ||
                    !item.TryGetProperty("embedding", out var embedding) ||
                    embedding.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("The embedding provider returned an invalid response.");

                var values = embedding.EnumerateArray()
                    .Select(value => value.TryGetSingle(out var number)
                        ? number
                        : throw new InvalidOperationException("The embedding provider returned an invalid response."))
                    .ToArray();
                return new { Index = itemIndex, Values = values };
            })
            .OrderBy(item => item.Index)
            .ToArray();

        if (items.Length != texts.Count ||
            !items.Select(item => item.Index).SequenceEqual(Enumerable.Range(0, texts.Count)) ||
            items.Length == 0 ||
            items[0].Values.Length == 0 ||
            items.Any(item => item.Values.Length != items[0].Values.Length ||
                              item.Values.Any(value => !float.IsFinite(value))))
            throw new InvalidOperationException("The embedding provider returned an invalid response.");

        return items.Select(item => item.Values).ToArray();
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string question,
        CancellationToken cancellationToken)
    {
        using var responseJson = await SendAsync(
            _options.ChatApiUrl,
            new
            {
                model = _options.ChatModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = question }
                },
                temperature = 0.1
            },
            cancellationToken);

        if (!responseJson.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("The chat provider returned an invalid response.");

        var response = content.GetString();
        return string.IsNullOrWhiteSpace(response)
            ? throw new InvalidOperationException("The AI provider returned an empty answer.")
            : response.Trim();
    }

    private async Task<JsonDocument> SendAsync(
        string endpoint,
        object body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Configure CoachingAssistant:ApiKey before using the assistant.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The assistant provider endpoint must be an HTTPS URL.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"The AI provider returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }
}
