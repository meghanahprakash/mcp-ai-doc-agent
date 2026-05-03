using System.ComponentModel;
using DocAgent.Utils;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class GitPlugin
{
    [KernelFunction, Description("Returns a newline-separated list of files changed on the current branch relative to the base branch.")]
    public string GetChangedFiles([Description("Absolute path to the local git repository.")] string repoPath)
    {
        var files = GitHelper.GetChangedFiles(repoPath);
        return string.Join("\n", files);
    }
}
