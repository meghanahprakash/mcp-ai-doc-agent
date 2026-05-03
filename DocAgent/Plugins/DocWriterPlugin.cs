using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DocAgent.Plugins;

public class DocWriterPlugin
{
    [KernelFunction, Description("Writes documentation content to docs/README.md.")]
    public string Write([Description("Markdown documentation content to write.")] string content)
    {
        Directory.CreateDirectory("docs");
        File.WriteAllText("docs/README.md", content);
        return "Saved";
    }
}
