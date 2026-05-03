
namespace DocAgent.AI;

public interface IAIProvider
{
    Task<string> GenerateAsync(string prompt);
}
