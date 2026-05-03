using DocAgent.Utils;

namespace DocAgent.Plugins;

public class GitPlugin
{
    public string GetChangedFiles(string repoPath)
    {
        var files = GitHelper.GetChangedFiles(repoPath);
        return string.Join("\n", files);
    }
}
