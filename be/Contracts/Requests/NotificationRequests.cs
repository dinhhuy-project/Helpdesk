namespace be.Contracts.Requests;

public sealed class NotificationSearchRequest : AccountPageRequest
{
    public bool? Unread { get; init; }
}
