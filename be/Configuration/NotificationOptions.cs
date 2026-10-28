using System.ComponentModel.DataAnnotations;

namespace be.Configuration;

public sealed class NotificationOptions
{
    public bool DispatchEnabled { get; set; } = true;
    [Range(1, 60)] public int PollSeconds { get; set; } = 2;
    [Range(1, 100)] public int BatchSize { get; set; } = 20;
}
