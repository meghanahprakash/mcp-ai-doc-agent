using DocAgent.AI;
using DocAgent.Utils;

namespace DocAgent.Agents;

public class PlannerAgent
{
    private readonly IAIProvider _ai;

    public PlannerAgent(IAIProvider ai)
    {
        _ai = ai;
    }

    public async Task<string> Plan(string files)
    {
        var template = PromptLoader.Load("planner");
        var prompt = PromptLoader.Inject(template, files);
        return await _ai.GenerateAsync(prompt);
    }
}
