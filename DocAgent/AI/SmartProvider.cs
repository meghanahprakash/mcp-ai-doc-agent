namespace DocAgent.AI
{
    // Placeholder for a smart provider that could combine multiple providers
    public class SmartProvider : IAIProvider
    {
        public Task<string> GenerateAsync(string prompt)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
