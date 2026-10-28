using be.Application.Features.Tickets;

namespace be.Infrastructure.Storage;

internal static class AttachmentBuffers
{
    internal static FileStream Create() => new(Path.Combine(Path.GetTempPath(), "helpdesk-file-" + Guid.NewGuid().ToString("N")),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
    internal static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += read;
            if (total > expected) throw new TicketOperationException(TicketError.InvalidFile);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != expected) throw new TicketOperationException(TicketError.InvalidFile);
    }
}
