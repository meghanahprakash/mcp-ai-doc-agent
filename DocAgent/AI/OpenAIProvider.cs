using Microsoft.SemanticKernel;

namespace DocAgent.AI
{
    public class OpenAIProvider : IAIProvider
    {
    private readonly Kernel _kernel;

    public OpenAIProvider(string apiKey)
    {
        _kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: "gpt-4o-mini",
                apiKey: apiKey)
            .Build();
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.ToString();
    }
}
}