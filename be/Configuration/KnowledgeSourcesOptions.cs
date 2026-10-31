namespace be.Configuration;

public sealed class KnowledgeSourcesOptions
{
    public const string Section = "KnowledgeSources";
    public bool WikipediaEnabled { get; set; } = true;
    public string UserAgent { get; set; } = "HelpdeskCommunity/1.0";
    public BookStackSourceOptions BookStack { get; set; } = new();
}

public sealed class BookStackSourceOptions
{
    public bool Enabled { get; set; }
    // Stable identifier: changing it deliberately creates a different import namespace.
    public string SourceId { get; set; } = "support-wiki";
    public string BaseUrl { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public string TokenId { get; set; } = "";
    public string TokenSecret { get; set; } = "";
    // A dedicated export book limits the connector's scope, in addition to wiki permissions.
    public int BookId { get; set; }
    public string Attribution { get; set; } = "Tài liệu do đơn vị quản lý; người duyệt cần xác nhận quyền công khai.";
    public string License { get; set; } = "Theo quyền sử dụng được đơn vị cho phép";
    public bool AllowHttp { get; set; }
}
