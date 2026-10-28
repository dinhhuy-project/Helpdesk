using be.Domain.Entities;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Notifications;

public sealed class TicketNotificationWriter(HelpdeskDbContext database, TicketScope scope)
{
    public async Task SaveChangesAsync(Guid actorId, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Notifications require the mutation transaction.");
        var activities = database.ChangeTracker.Entries<TicketActivity>().Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity).ToArray();
        await database.SaveChangesAsync(cancellationToken);
        foreach (var activity in activities.Where(activity => NotificationKinds.IsKnown(activity.Action)))
        {
            var isInternal = NotificationKinds.Internal.Contains(activity.Action);
            var recipients = await scope.Readers().Where(reader => reader.TicketId == activity.TicketId && reader.UserId != actorId &&
                (!isInternal || reader.CanReadInternal)).Select(reader => reader.UserId).Distinct().ToListAsync(cancellationToken);
            var existing = await database.Notifications.Where(x => x.TicketActivityId == activity.Id).Select(x => x.UserId).ToListAsync(cancellationToken);
            database.Notifications.AddRange(recipients.Except(existing).Select(userId => new Notification
            {
                UserId = userId, TicketId = activity.TicketId, TicketActivityId = activity.Id,
                Title = NotificationKinds.Title(activity.Action), Content = "Mở Ticket để xem cập nhật trong phạm vi được phép.", CreatedAt = activity.CreatedAt
            }));
        }
        if (database.ChangeTracker.Entries<Notification>().Any(x => x.State == EntityState.Added))
            await database.SaveChangesAsync(cancellationToken);
    }
}
