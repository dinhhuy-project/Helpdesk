using System.Text.Json;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;

namespace be.Infrastructure.Tickets;

internal static class TicketActivityProjection
{
    internal static TicketActivityDetails Map(TicketActivity activity) => new(activity.Id, activity.TicketId, activity.ActorId,
        activity.Action, activity.CreatedAt, Data(activity.Action, activity.OldValue), Data(activity.Action, activity.NewValue));

    // Never return raw audit JSON. Each registered action exposes only its own public fields;
    // comment bodies, arbitrary legacy fields and payloads from future actions cannot escape.
    private static TicketActivityData? Data(string action, string? json)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object) return null;
            return action switch
            {
                TicketActivityActions.Created => new(TicketNumber: Number(value, "TicketNumber"), RequesterId: Id(value, "RequesterId"),
                    CreatedByUserId: Id(value, "CreatedByUserId"), CategoryId: Id(value, "CategoryId"),
                    Status: EnumText<TicketStatus>(value, "Status"), Priority: EnumText<TicketPriority>(value, "Priority")),
                TicketActivityActions.Assigned => new(AssignedTeamId: Id(value, "AssignedTeamId"), AssignedAgentId: Id(value, "AssignedAgentId")),
                TicketActivityActions.StatusChanged => new(Status: EnumText<TicketStatus>(value, "Status"),
                    ResolutionSummary: Text(value, "ResolutionSummary", 4_000), ResolvedAt: Time(value, "ResolvedAt"), ClosedAt: Time(value, "ClosedAt")),
                TicketActivityActions.PublicCommentAdded or TicketActivityActions.InternalNoteAdded => new(CommentId: Id(value, "CommentId"),
                    IsInternal: action == TicketActivityActions.InternalNoteAdded),
                TicketActivityActions.PublicAttachmentAdded or TicketActivityActions.InternalAttachmentAdded => new(AttachmentId: Id(value, "AttachmentId"),
                    CommentId: Id(value, "CommentId"), IsInternal: action == TicketActivityActions.InternalAttachmentAdded),
                TicketActivityActions.SlaFirstResponseBreached or TicketActivityActions.SlaResolutionBreached => new(DueAt: Time(value, "DueAt"), DetectedAt: Time(value, "DetectedAt")),
                _ => null
            };
        }
        catch (JsonException) { return null; }
    }
    private static string? Text(JsonElement value, string name, int limit) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() is { } text && text.Length <= limit ? text : null;
    private static Guid? Id(JsonElement value, string name) => Guid.TryParse(Text(value, name, 36), out var id) ? id : null;
    private static long? Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number) ? number : null;
    private static string? EnumText<T>(JsonElement value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(Text(value, name, 30), out var item) && Enum.IsDefined(item) ? item.ToString() : null;
    private static DateTime? Time(JsonElement value, string name) => DateTimeOffset.TryParse(Text(value, name, 64),
        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time) ? time.UtcDateTime : null;
}
