using System.Net.Http.Headers;
using System.Text;
using be.Application.Common.Storage;
using be.Application.Features.Tickets;
using be.Configuration;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Storage;

public sealed class AttachmentFileValidator(IOptions<MinioOptions> settings) : IAttachmentFileValidator
{
    private static readonly IReadOnlyList<AttachmentFileType> FileTypes = Array.AsReadOnly(new[] {
        new AttachmentFileType(".txt", "text/plain"), new AttachmentFileType(".pdf", "application/pdf"),
        new AttachmentFileType(".png", "image/png"), new AttachmentFileType(".jpg", "image/jpeg"), new AttachmentFileType(".jpeg", "image/jpeg") });
    public AttachmentUploadPolicy Policy => new(settings.Value.Enabled, settings.Value.MaxFileSizeBytes,
        Limits.MaxFiles, Limits.MaxBytes, FileTypes);
    public Stream CreateDownloadBuffer() => AttachmentBuffers.Create();
    public AttachmentLimits Limits => new(settings.Value.MaxFilesPerVisibility, settings.Value.MaxTotalSizeMiBPerVisibility * 1024L * 1024);
    public UploadTicketAttachment ValidateMetadata(UploadTicketAttachment command)
    {
        if (!settings.Value.Enabled) throw new TicketOperationException(TicketError.StorageUnavailable);
        if (command.SizeBytes <= 0 || command.SizeBytes > settings.Value.MaxFileSizeBytes)
            throw new TicketOperationException(TicketError.InvalidFile);
        var name = command.FileName;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim() || name.StartsWith('.') || name.EndsWith('.') ||
            name.Contains("..") || name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            throw new TicketOperationException(TicketError.InvalidFile);
        var expected = FileTypes.SingleOrDefault(type => type.Extension == Path.GetExtension(name).ToLowerInvariant())?.ContentType;
        if (expected is null || !MediaTypeHeaderValue.TryParse(command.ContentType, out var media) ||
            !string.Equals(media.MediaType, expected, StringComparison.OrdinalIgnoreCase))
            throw new TicketOperationException(TicketError.UnsupportedFile);
        return command with { ContentType = expected };
    }
    public async Task<Stream> BufferAsync(Stream input, UploadTicketAttachment command, CancellationToken cancellationToken)
    {
        var buffer = AttachmentBuffers.Create();
        try
        {
            await AttachmentBuffers.CopyBoundedAsync(input, buffer, command.SizeBytes, cancellationToken);
            buffer.Position = 0;
            var header = new byte[8];
            var read = await buffer.ReadAsync(header, cancellationToken);
            var valid = command.ContentType switch
            {
                "application/pdf" => read >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
                "image/png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "image/jpeg" => read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255,
                "text/plain" => true,
                _ => false
            };
            if (!valid) throw new TicketOperationException(TicketError.InvalidFile);
            if (command.ContentType == "text/plain")
            {
                buffer.Position = 0;
                using var reader = new StreamReader(buffer, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
                var chars = new char[4096];
                int count;
                while ((count = await reader.ReadAsync(chars.AsMemory(), cancellationToken)) != 0)
                    if (chars.Take(count).Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                        throw new TicketOperationException(TicketError.InvalidFile);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch (DecoderFallbackException) { await buffer.DisposeAsync(); throw new TicketOperationException(TicketError.InvalidFile); }
        catch { await buffer.DisposeAsync(); throw; }
    }
}
