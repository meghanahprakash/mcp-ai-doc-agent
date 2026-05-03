namespace DocAgent.Utils
{
    public static class PromptLoader
    {
        public static string Load(string name)
        {
            // Try relative to current directory first
            var path = Path.Combine("Prompts", $"{name}.txt");
            
            // If not found, try relative to application base directory
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{name}.txt");
            }

            if (!File.Exists(path))
                throw new Exception($"Prompt not found: {name}. Searched in: {Path.Combine("Prompts", $"{name}.txt")} and {path}");

            return File.ReadAllText(path);
        }

        public static string Inject(string template, string input)
        {
            return template.Replace("{{input}}", input);
        }
    }
}
