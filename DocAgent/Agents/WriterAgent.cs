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

        return await _ai.GenerateAsync(prompt);
    }
}
