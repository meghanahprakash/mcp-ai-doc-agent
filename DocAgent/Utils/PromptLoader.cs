namespace DocAgent.Utils
{
    public static class PromptLoader
    {
        public static string Load(string name)
        {
            var path = Path.Combine("Prompts", $"{name}.txt");

            if (!File.Exists(path))
                throw new Exception($"Prompt not found: {name}");

            return File.ReadAllText(path);
        }

        public static string Inject(string template, string input)
        {
            return template.Replace("{{input}}", input);
        }
    }
}
