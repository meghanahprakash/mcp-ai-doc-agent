using DocAgent.AI;
using DocAgent.Utils;

namespace DocAgent.Agents;

public class WriterAgent
{
    private readonly IAIProvider _ai;

    public WriterAgent(IAIProvider ai)
    {
        _ai = ai;
    }

    public async Task<string> Generate(string input)
    {
        var template = PromptLoader.Load("writer");
        var prompt = PromptLoader.Inject(template, input);
        var result = await _ai.GenerateAsync(prompt);

        if (NeedsRewrite(result))
        {
            var retryPrompt =
                "Generate a strict JSON documentation summary pack now. Do not ask for more information. " +
                "Do not include markdown fences or preamble. Return only a JSON object with document-specific summaries.\n\n" +
                "INPUT:\n" + input;
            result = await _ai.GenerateAsync(retryPrompt);
        }

        return result;
    }

    private static bool NeedsRewrite(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return true;
        }

        var normalized = output.Trim();
        return normalized.Contains("please provide", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("i'm ready", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("I can", StringComparison.OrdinalIgnoreCase)
            || normalized.Length < 100
            || (!normalized.StartsWith("{", StringComparison.Ordinal) && !normalized.Contains("project_overview", StringComparison.OrdinalIgnoreCase));
    }
}
