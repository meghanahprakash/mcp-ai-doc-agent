namespace DocAgent.Utils
{
    public static class PromptLoader
    {
        public static string Load(string name)
        {
            // Prefer app base directory to avoid cwd-dependent prompt resolution.
            var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{name}.txt");

            // Fallback to current directory for local development scenarios.
            if (!File.Exists(path))
            {
                path = Path.Combine("Prompts", $"{name}.txt");
            }

            if (!File.Exists(path))
                throw new Exception($"Prompt not found: {name}. Searched in: {Path.Combine("Prompts", $"{name}.txt")} and {path}");

            return File.ReadAllText(path);
        }

        public static string? LoadOptional(string name)
        {
            try
            {
                return Load(name);
            }
            catch
            {
                return null;
            }
        }

        public static string Inject(string template, string input)
        {
            return template.Replace("{{input}}", input);
        }
    }
}
