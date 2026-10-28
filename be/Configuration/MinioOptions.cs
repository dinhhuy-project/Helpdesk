using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace be.Configuration;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "localhost:9000";
    public string? HostOverride { get; set; }
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "helpdesk-attachments";
    public bool UseSsl { get; set; }
    public bool CleanupEnabled { get; set; } = true;
    [Range(1, 10)] public int MaxFileSizeMiB { get; set; } = 10;
    [Range(1, 100)] public int MaxFilesPerVisibility { get; set; } = 20;
    [Range(1, 500)] public int MaxTotalSizeMiBPerVisibility { get; set; } = 100;
    [Range(1, 1000)] public int UploadsPerMinute { get; set; } = 10;
    public long MaxFileSizeBytes => MaxFileSizeMiB * 1024L * 1024;
    public static bool IsValid(MinioOptions options)
    {
        if (!options.Enabled) return true;
        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey) ||
            !Regex.IsMatch(options.Bucket, "^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$") || options.Bucket.Contains("..")) return false;
        if (!Uri.TryCreate("http://" + options.Endpoint, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "") return false;
        return string.IsNullOrEmpty(options.HostOverride) || Uri.CheckHostName(options.HostOverride) != UriHostNameType.Unknown;
    }
}
