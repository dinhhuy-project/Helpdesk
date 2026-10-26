using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Categories;

internal static class TicketCategoryAvailability
{
    // Shared by lookup, parent validation and Ticket creation. Distinct IDs bound malformed cycles.
    internal static IQueryable<Guid> Query(HelpdeskDbContext database) => database.Database.SqlQueryRaw<Guid>("""
        WITH RECURSIVE available(id) AS (
            SELECT id FROM helpdesk.ticket_categories WHERE parent_id IS NULL AND is_active
            UNION
            SELECT child.id FROM helpdesk.ticket_categories child JOIN available parent ON child.parent_id = parent.id WHERE child.is_active
        ) SELECT id AS "Value" FROM available
        """);
}
