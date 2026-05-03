using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class ValidationPlugin
{
    [KernelFunction, Description("Validates documentation content for hallucination risk.")]
    public string Validate([Description("Documentation text to validate.")] string doc)
    {
        if (doc.Contains("assume") || doc.Contains("guess"))
            return "hallucination risk";

        return "valid";
    }
}
