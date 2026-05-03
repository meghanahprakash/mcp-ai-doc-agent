using DocAgent.AI;
using DocAgent.Utils;

namespace DocAgent.Agents;

public class AnalyzerAgent
{
    private readonly IAIProvider _ai;

    public AnalyzerAgent(IAIProvider ai)
    {
        _ai = ai;
    }

    public async Task<string> Analyze(string code)
    {
        var template = PromptLoader.Load("analyzer");

        var prompt = PromptLoader.Inject(template, code);

        return await _ai.GenerateAsync(prompt);
    }
}
