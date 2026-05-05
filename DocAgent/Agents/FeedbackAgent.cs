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

    public async Task<string> Refine(string doc, string? previousFeedback = null)
    {
        var template = PromptLoader.Load("feedback");
        var combinedInput =
            $"Generated Documentation:\n{doc}\n\n" +
            $"Previous Feedback (from prior run):\n{(string.IsNullOrWhiteSpace(previousFeedback) ? "None" : previousFeedback)}";
        var prompt = PromptLoader.Inject(template, combinedInput);
        return await _ai.GenerateAsync(prompt);
    }
}
