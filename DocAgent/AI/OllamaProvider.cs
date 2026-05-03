using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DocAgent.AI
{
    public class OllamaProvider : IAIProvider
    {
    private const string GenerateEndpoint = "http://localhost:11434/api/generate";
    private const string HealthEndpoint = "http://localhost:11434/api/tags";

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
            response = await SendGenerateRequestAsync(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var autoStarted = await TryStartOllamaAsync();
            if (autoStarted)
            {
                try
                {
                    response = await SendGenerateRequestAsync(body);
                }
                catch (Exception retryEx) when (retryEx is HttpRequestException or TaskCanceledException)
                {
                    throw new InvalidOperationException(
                        "Cannot reach Ollama at http://localhost:11434 after auto-start attempt. Ensure Ollama is installed and runnable with 'ollama serve'.",
                        retryEx);
                }
            }
            else
            {
            throw new InvalidOperationException(
                "Cannot reach Ollama at http://localhost:11434. Auto-start failed; ensure Ollama is installed and running (ollama serve).",
                ex);
            }
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.GetProperty("response").GetString()
            ?? string.Empty;
    }

    private Task<HttpResponseMessage> SendGenerateRequestAsync(object body)
    {
        return _client.PostAsync(
            GenerateEndpoint,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
    }

    private async Task<bool> TryStartOllamaAsync()
    {
        if (await IsOllamaReachableAsync())
        {
            return true;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            if (await IsOllamaReachableAsync())
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsOllamaReachableAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            var response = await _client.GetAsync(HealthEndpoint, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
}
