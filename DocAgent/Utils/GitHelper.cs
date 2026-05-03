using System.Diagnostics;
namespace DocAgent.Utils;

public static class GitHelper
{
    public static string RunGitCommand(string command, string workingDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }

    public static string GetCurrentBranch(string repoPath)
    {
        try
        {
            var branch = RunGitCommand(
                "rev-parse --abbrev-ref HEAD",
                repoPath
            ).Trim();

            if (branch == "HEAD")
            {
                // Detached HEAD → use commit
                branch = RunGitCommand(
                    "rev-parse HEAD",
                    repoPath
                ).Trim();
            }

            return branch;
        }
        catch
        {
            return "HEAD";
        }
    }

    public static string GetBaseBranch(string repoPath)
    {
        var branches = RunGitCommand("branch -r", repoPath);

        if (branches.Contains("origin/main"))
            return "origin/main";

        if (branches.Contains("origin/master"))
            return "origin/master";

        Console.WriteLine("⚠️ No origin/main or origin/master found. Using HEAD~1");

        return "HEAD~1";
    }

    public static List<string> GetChangedFiles(string repoPath)
    {

        var currentBranch = GetCurrentBranch(repoPath);
        var baseBranch = GetBaseBranch(repoPath);

        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            Console.WriteLine("⚠️ Could not detect branch. Using HEAD.");
            currentBranch = "HEAD";
        }

        Console.WriteLine($"🔍 Comparing {currentBranch} → {baseBranch}");

        var diff = RunGitCommand(
            $"diff --name-only {baseBranch}...HEAD",
            repoPath
        );

        if (string.IsNullOrWhiteSpace(diff))
        {
            Console.WriteLine("⚠️ No diff output. Using fallback.");
            diff = RunGitCommand("ls-files", repoPath);
        }

        return diff
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }


    public static string CloneRepo(string repoUrl)
    {
        var folder = $"temp_repo_{Guid.NewGuid()}";

        RunGitCommand($"clone {repoUrl} {folder}", Directory.GetCurrentDirectory());

        return Path.Combine(Directory.GetCurrentDirectory(), folder);
    }
}