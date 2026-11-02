namespace be.Configuration;

public sealed class LocalAssistantOptions
{
    public const string Section = "LocalAssistant";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434/";
    public string Model { get; set; } = "qwen3:1.7b";
    public int TimeoutSeconds { get; set; } = 60;
}
