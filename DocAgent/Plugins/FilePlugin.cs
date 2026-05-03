using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class FilePlugin
{
    [KernelFunction, Description("Reads the full text content of a file at the given path.")]
    public string ReadFile(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
