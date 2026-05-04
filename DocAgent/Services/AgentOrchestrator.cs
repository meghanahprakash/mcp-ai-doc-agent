using DocAgent.Utils;
using DocAgent.AI;
using DocAgent.Agents;

namespace DocAgent.Services
{
public class AgentOrchestrator
{
    private const int MaxFileBytes = 200_000;

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

        // Normalize incoming changed-file paths from CI/git sources.
        changedFiles = changedFiles
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Replace('\\', '/').Trim())
            .Select(f => f.StartsWith("./", StringComparison.Ordinal) ? f[2..] : f)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // ✅ Read only changed files
        foreach (var file in changedFiles)
        {
            var normalizedFile = file.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(resolvedRepoPath, normalizedFile);

            if (File.Exists(fullPath))
            {
                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > MaxFileBytes)
                    {
                        Console.WriteLine($"⚠️ Skipping large file: {file} ({fileInfo.Length} bytes)");
                        continue;
                    }

                    var content = File.ReadAllText(fullPath);
                    if (LooksBinary(content))
                    {
                        Console.WriteLine($"⚠️ Skipping binary-like file: {file}");
                        continue;
                    }

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
        var writerInput =
            $"Plan:\n{plan}\n\nAnalysis:\n{analysis}\n\nFiles:\n{string.Join("\n", changedFiles)}";
        var docs = await writer.Generate(writerInput);

        var resolvedOutputDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.Combine(resolvedRepoPath, "docs")
            : outputDir;

        Directory.CreateDirectory(resolvedOutputDir);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "plan.md"), plan);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "analysis.md"), analysis);
        var outputFile = Path.Combine(resolvedOutputDir, "README.md");
        File.WriteAllText(outputFile, docs);

        Console.WriteLine($"✅ Documentation generated at: {outputFile}");
    }

    private static bool LooksBinary(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        var sampleSize = Math.Min(content.Length, 2000);
        var nonPrintableCount = 0;

        for (var i = 0; i < sampleSize; i++)
        {
            var c = content[i];
            if (char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            {
                nonPrintableCount++;
            }
        }

        return (double)nonPrintableCount / sampleSize > 0.1;
    }

}

}
