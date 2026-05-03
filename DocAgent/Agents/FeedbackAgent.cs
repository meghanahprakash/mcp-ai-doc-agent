using DocAgent.AI;
using DocAgent.Utils;

namespace DocAgent.Agents;

public class FeedbackAgent
{
    private readonly IAIProvider _ai;

    public FeedbackAgent(IAIProvider ai)
    {
        _ai = ai;
    }

    public async Task<string> Refine(string doc)
    {
        var template = PromptLoader.Load("feedback");
        var prompt = PromptLoader.Inject(template, doc);
        return await _ai.GenerateAsync(prompt);
    }
}
