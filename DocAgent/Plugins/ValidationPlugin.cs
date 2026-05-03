namespace DocAgent.Plugins;

public class ValidationPlugin
{
    public string Validate(string doc)
    {
        if (doc.Contains("assume") || doc.Contains("guess"))
            return "hallucination risk";

        return "valid";
    }
}
