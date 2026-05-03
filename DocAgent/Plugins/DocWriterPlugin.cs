namespace DocAgent.Plugins;

public class DocWriterPlugin
{
    public string Write(string content)
    {
        Directory.CreateDirectory("docs");
        File.WriteAllText("docs/README.md", content);
        return "Saved";
    }
}
