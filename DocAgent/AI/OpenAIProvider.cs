using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DocAgent.Utils;

namespace DocAgent.AI
{
    public class OpenAIProvider : IAIProvider
    {
    private readonly string _apiKey;
    private static readonly string SystemPrompt = PromptLoader.LoadOptional("system-prompt") ?? string.Empty;
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    public OpenAIProvider(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = prompt }
            },
            temperature = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _client.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? string.Empty;
    }
}
}