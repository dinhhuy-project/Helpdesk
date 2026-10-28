using System.Text;
using be.Application.Features.Tickets;
using be.Configuration;
using be.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class AttachmentValidationTests
{
    [Theory]
    [InlineData("report.pdf", "application/pdf", "%PDF-1.7", true)]
    [InlineData("readme.txt", "text/plain", "Tiếng Việt", true)]
    [InlineData("readme.txt", "text/plain", "a\0b", false)]
    public async Task AllowedFormatsRequireMatchingContentAndBoundedStreams(string name, string type, string text, bool accepted)
    {
        var options = new MinioOptions { Enabled = true };
        var validator = new AttachmentFileValidator(Options.Create(options));
        var bytes = Encoding.UTF8.GetBytes(text);
        var command = validator.ValidateMetadata(new(name, type, bytes.Length));
        if (accepted)
        {
            await using var file = await validator.BufferAsync(new MemoryStream(bytes), command, CancellationToken.None);
            Assert.Equal(bytes.Length, file.Length);
            Assert.Equal(0, file.Position);
        }
        else await Assert.ThrowsAsync<TicketOperationException>(() => validator.BufferAsync(new MemoryStream(bytes), command, CancellationToken.None));
        await Assert.ThrowsAsync<TicketOperationException>(() => validator.BufferAsync(new MemoryStream(bytes), command with { SizeBytes = bytes.Length + 1 }, CancellationToken.None));
    }
    [Fact]
    public void SizeAndConfigurationLimitsFailClosed()
    {
        var validator = new AttachmentFileValidator(Options.Create(new MinioOptions { Enabled = true, MaxFileSizeMiB = 1 }));
        Assert.Throws<TicketOperationException>(() => validator.ValidateMetadata(new("report.txt", "text/plain", 1024 * 1024 + 1)));
        Assert.Throws<TicketOperationException>(() => validator.ValidateMetadata(new("report.txt", "text/plain", 0)));
        Assert.True(MinioOptions.IsValid(new()));
        Assert.False(MinioOptions.IsValid(new() { Enabled = true }));
        Assert.False(MinioOptions.IsValid(new() { Enabled = true, AccessKey = "test", SecretKey = "test", Endpoint = "http://invalid/path" }));
    }
}
