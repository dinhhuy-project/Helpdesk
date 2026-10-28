using System.ComponentModel.DataAnnotations;

namespace be.Configuration;

public sealed class SlaOptions
{
    public bool WorkerEnabled { get; set; } = true;
    [Range(1, 3600)] public int PollSeconds { get; set; } = 30;
    [Range(1, 100)] public int BatchSize { get; set; } = 20;
}
