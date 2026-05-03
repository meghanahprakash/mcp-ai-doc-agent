namespace DocAgent.Plugins;

public class RepoPlugin
{
    public string GetStructure(string path)
    {
        return string.Join("\n", Directory.GetFiles(path, "*", SearchOption.AllDirectories));
    }
}
