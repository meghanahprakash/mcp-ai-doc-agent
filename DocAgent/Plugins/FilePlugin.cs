namespace DocAgent.Plugins;

public class FilePlugin
{
    public string ReadFile(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
