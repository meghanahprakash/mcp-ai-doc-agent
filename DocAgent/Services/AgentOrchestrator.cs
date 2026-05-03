using DocAgent.Utils;
using DocAgent.AI;
using DocAgent.Agents;

namespace DocAgent.Services
{
public class AgentOrchestrator
{
    public async Task Run(string repoPath, string[] changedFiles, string outputDir)
    {
        var resolvedRepoPath = repoPath;

        // ✅ If remote repo → clone
        if (repoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("📦 Cloning repository...");
            resolvedRepoPath = GitHelper.CloneRepo(repoPath);
        }

            // ✅ Get changed files dynamically
        var fileContents = new List<string>();

        // ✅ If no files → fallback to full repo (optional)
        if (changedFiles == null || changedFiles.Length == 0)
        {
            Console.WriteLine("⚠️ No changed files provided, using full repo fallback");

            var allFiles = Directory.GetFiles(resolvedRepoPath, "*.*", SearchOption.AllDirectories);

            changedFiles = allFiles.Select(f => Path.GetRelativePath(resolvedRepoPath, f)).ToArray();
        }

        // ✅ Read only changed files
        foreach (var file in changedFiles)
        {
            var fullPath = Path.Combine(resolvedRepoPath, file);

            if (File.Exists(fullPath))
            {
                try
                {
                    var content = File.ReadAllText(fullPath);

                    fileContents.Add($"FILE: {file}\n{content}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Skipping file: {file}");
                    Console.WriteLine(ex.Message);
                }
            }
        }

        var ai = AIProviderFactory.Create();

        var planner = new PlannerAgent(ai);
        var analyzer = new AnalyzerAgent(ai);
        var writer = new WriterAgent(ai);

        var plan = await planner.Plan(string.Join("\n", changedFiles));
        var analysis = await analyzer.Analyze(string.Join("\n\n", fileContents));
        var docs = await writer.Generate(analysis);

        var resolvedOutputDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.Combine(resolvedRepoPath, "docs")
            : outputDir;

        Directory.CreateDirectory(resolvedOutputDir);
        var outputFile = Path.Combine(resolvedOutputDir, "README.md");
        File.WriteAllText(outputFile, docs);

        Console.WriteLine($"✅ Documentation generated at: {outputFile}");
    }

}

}
