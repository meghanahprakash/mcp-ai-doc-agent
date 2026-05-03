using DocAgent.AI;

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
        return await _ai.GenerateAsync(
            $"Generate documentation:\n{input}"
        );
    }
}
