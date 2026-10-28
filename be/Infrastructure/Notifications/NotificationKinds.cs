using be.Domain.Constants;

namespace be.Infrastructure.Notifications;

internal static class NotificationKinds
{
    internal static readonly string[] Public = [TicketActivityActions.Created, TicketActivityActions.Assigned,
        TicketActivityActions.StatusChanged, TicketActivityActions.PublicCommentAdded, TicketActivityActions.PublicAttachmentAdded,
        TicketActivityActions.SlaFirstResponseBreached, TicketActivityActions.SlaResolutionBreached];
    internal static readonly string[] Internal = [TicketActivityActions.InternalNoteAdded, TicketActivityActions.InternalAttachmentAdded];
    internal static bool IsKnown(string action) => Public.Contains(action) || Internal.Contains(action);
    internal static string Title(string action) => action switch
    {
        TicketActivityActions.Created => "Ticket mới",
        TicketActivityActions.Assigned => "Phân công Ticket đã thay đổi",
        TicketActivityActions.StatusChanged => "Trạng thái Ticket đã thay đổi",
        TicketActivityActions.PublicCommentAdded => "Ticket có bình luận mới",
        TicketActivityActions.InternalNoteAdded => "Ticket có ghi chú nội bộ mới",
        TicketActivityActions.PublicAttachmentAdded => "Ticket có file đính kèm mới",
        TicketActivityActions.InternalAttachmentAdded => "Ticket có file nội bộ mới",
        TicketActivityActions.SlaFirstResponseBreached => "Ticket đã vi phạm SLA phản hồi đầu tiên",
        TicketActivityActions.SlaResolutionBreached => "Ticket đã vi phạm SLA giải quyết",
        _ => "Ticket có cập nhật"
    };
}
