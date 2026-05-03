using DocAgent.Services;
using DocAgent.AI;

try
{
    var argsDict = ParseArgs(args);

    var repoPath = argsDict.ContainsKey("--repo")
        ? argsDict["--repo"]
        : Directory.GetCurrentDirectory();

    var provider = argsDict.ContainsKey("--provider")
        ? argsDict["--provider"]
        : "ollama";

    var outputDir = argsDict.ContainsKey("--output-dir")
        ? argsDict["--output-dir"]
        : Path.Combine(repoPath, "docs");

    // ✅ ✅ HANDLE CHANGED FILES SAFELY
    var changedFiles = argsDict.ContainsKey("--files")
        ? argsDict["--files"]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToArray()
        : Array.Empty<string>();

    Console.WriteLine($"📂 Repo: {repoPath}");
    Console.WriteLine($"🤖 Provider: {provider}");
    Console.WriteLine($"📝 Output Dir: {outputDir}");

    Console.WriteLine("📄 Changed Files:");
    foreach (var file in changedFiles)
    {
        Console.WriteLine($" - {file}");
    }

    Environment.SetEnvironmentVariable("AI_PROVIDER", provider);

    var orchestrator = new AgentOrchestrator();

    // ✅ PASS FILES INTO AGENT
    await orchestrator.Run(repoPath, changedFiles, outputDir);

    Console.WriteLine("✅ Done!");
}
catch (Exception ex)
{
    Console.WriteLine("❌ ERROR:");
    Console.WriteLine(ex.Message);
    Console.WriteLine(ex.StackTrace);
}

static Dictionary<string, string> ParseArgs(string[] arguments)
{
    var dict = new Dictionary<string, string>();

    for (var i = 0; i < arguments.Length; i++)
    {
        var key = arguments[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var value = i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[++i]
            : string.Empty;

        dict[key] = value;
    }

    return dict;
}
