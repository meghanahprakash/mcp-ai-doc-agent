using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class RepoPlugin
{
    [KernelFunction, Description("Returns a newline-separated list of all files in the repository.")]
    public string GetStructure(string path)
    {
        return string.Join("\n", Directory.GetFiles(path, "*", SearchOption.AllDirectories));
    }
}
