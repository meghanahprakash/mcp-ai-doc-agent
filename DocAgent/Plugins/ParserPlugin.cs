using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class ParserPlugin
{
    [KernelFunction, Description("Parses the structure of source code and returns a summary.")]
    public string Parse([Description("Source code text to parse.")] string code)
    {
        return "Parsed structure";
    }
}
