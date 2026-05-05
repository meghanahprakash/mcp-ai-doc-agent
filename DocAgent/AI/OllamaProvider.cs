using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DocAgent.Utils;

namespace DocAgent.AI
{
    public class OllamaProvider : IAIProvider
    {
    private const string DefaultBaseUrl = "http://localhost:11434";
    private static readonly string SystemPrompt = PromptLoader.LoadOptional("system-prompt") ?? string.Empty;

    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    public async Task<string> GenerateAsync(string prompt)
    {
        var body = new
        {
            model = "llama3",
            system = SystemPrompt,
            prompt = prompt,
            stream = false
        };

        Exception? lastConnectionError = null;
        foreach (var baseUrl in GetOllamaBaseUrls())
        {
            HttpResponseMessage response;
            try
            {
                response = await SendGenerateRequestAsync(baseUrl, body);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                var parsed = JsonDocument.Parse(json);
                return parsed.RootElement.GetProperty("response").GetString()
                    ?? string.Empty;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastConnectionError = ex;

                var autoStarted = await TryStartOllamaAsync(baseUrl);
                if (!autoStarted)
                {
                    continue;
                }

                try
                {
                    response = await SendGenerateRequestAsync(baseUrl, body);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();

                    var parsed = JsonDocument.Parse(json);
                    return parsed.RootElement.GetProperty("response").GetString()
                        ?? string.Empty;
                }
                catch (Exception retryEx) when (retryEx is HttpRequestException or TaskCanceledException)
                {
                    lastConnectionError = retryEx;
                }
            }
        }

        throw new InvalidOperationException(
            "Cannot reach Ollama. Tried configured endpoints from OLLAMA_BASE_URLS/OLLAMA_BASE_URL or default http://localhost:11434. " +
            "If running app in WSL and Ollama on Windows host, set OLLAMA_BASE_URL to a reachable host URL.",
            lastConnectionError);
    }

    private Task<HttpResponseMessage> SendGenerateRequestAsync(string baseUrl, object body)
    {
        return _client.PostAsync(
            BuildEndpoint(baseUrl, "/api/generate"),
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
    }

    private async Task<bool> TryStartOllamaAsync(string baseUrl)
    {
        if (await IsOllamaReachableAsync(baseUrl))
        {
            return true;
        }

        var started = false;
        foreach (var executablePath in GetOllamaExecutableCandidates())
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                started = true;
                break;
            }
            catch
            {
                // Keep trying next candidate path.
            }
        }

        if (!started)
        {
            return false;
        }

        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            if (await IsOllamaReachableAsync(baseUrl))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsOllamaReachableAsync(string baseUrl)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            var response = await _client.GetAsync(BuildEndpoint(baseUrl, "/api/tags"), cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetOllamaBaseUrls()
    {
        var candidates = new List<string>();

        var urls = Environment.GetEnvironmentVariable("OLLAMA_BASE_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            candidates.AddRange(urls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var singleUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
        if (!string.IsNullOrWhiteSpace(singleUrl))
        {
            candidates.Add(singleUrl);
        }

        candidates.Add(DefaultBaseUrl);
        candidates.Add("http://127.0.0.1:11434");

        return candidates
            .Where(c => Uri.TryCreate(c, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildEndpoint(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static IEnumerable<string> GetOllamaExecutableCandidates()
    {
        var candidates = new List<string>();

        var envPath = Environment.GetEnvironmentVariable("OLLAMA_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            candidates.Add(envPath);
        }

        candidates.Add("ollama");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AddIfExists(candidates, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe"));

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            AddIfExists(candidates, Path.Combine(programFiles, "Ollama", "ollama.exe"));
        }
        else
        {
            AddIfExists(candidates, "/usr/local/bin/ollama");
            AddIfExists(candidates, "/usr/bin/ollama");

            var windowsUsersRoot = "/mnt/c/Users";
            if (Directory.Exists(windowsUsersRoot))
            {
                try
                {
                    foreach (var userDir in Directory.GetDirectories(windowsUsersRoot))
                    {
                        var wslOllamaPath = Path.Combine(userDir, "AppData", "Local", "Programs", "Ollama", "ollama.exe");
                        AddIfExists(candidates, wslOllamaPath);
                    }
                }
                catch
                {
                    // Ignore directory enumeration issues.
                }
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddIfExists(List<string> candidates, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            candidates.Add(path);
        }
    }
}
}
