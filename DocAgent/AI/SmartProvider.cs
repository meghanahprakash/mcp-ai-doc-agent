namespace DocAgent.AI
{
    public class SmartProvider : IAIProvider
    {
        public async Task<string> GenerateAsync(string prompt)
        {
            var errors = new List<string>();

            // Prefer local Ollama first for cost and latency when available.
            try
            {
                var ollama = new OllamaProvider();
                return await ollama.GenerateAsync(prompt);
            }
            catch (Exception ex)
            {
                errors.Add($"Ollama: {ex.Message}");
            }

            var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(openAiApiKey))
            {
                try
                {
                    var openAi = new OpenAIProvider(openAiApiKey);
                    return await openAi.GenerateAsync(prompt);
                }
                catch (Exception ex)
                {
                    errors.Add($"OpenAI: {ex.Message}");
                }
            }
            else
            {
                errors.Add("OpenAI: OPENAI_API_KEY environment variable is not set.");
            }

            throw new InvalidOperationException(
                "All AI providers failed. " + string.Join(" | ", errors));
        }
    }
}
