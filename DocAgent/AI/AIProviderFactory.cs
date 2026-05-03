
namespace DocAgent.AI
{
    public static class AIProviderFactory
    {
    public static IAIProvider Create()
    {
        var provider = Environment.GetEnvironmentVariable("AI_PROVIDER");

        return provider switch
        {
            "ollama" => new OllamaProvider(),
            "smart" => new SmartProvider(),

            "openai" => new OpenAIProvider(
                Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.")
            ),

            _ => new SmartProvider()
        };
    }
}
}

