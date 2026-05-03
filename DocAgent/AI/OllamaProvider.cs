using System.Text;
using System.Text.Json;

namespace DocAgent.AI
{
    public class OllamaProvider : IAIProvider
    {
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    public async Task<string> GenerateAsync(string prompt)
    {
        var body = new
        {
            model = "llama3",
            prompt = prompt,
            stream = false
        };

        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsync(
                "http://localhost:11434/api/generate",
                new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json")
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Cannot reach Ollama at http://localhost:11434. Ensure Ollama is running (ollama serve).", ex);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.GetProperty("response").GetString()
            ?? string.Empty;
    }
}
}
