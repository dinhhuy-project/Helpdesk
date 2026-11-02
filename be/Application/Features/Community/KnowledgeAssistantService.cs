using be.Configuration;
using Microsoft.Extensions.Options;

namespace be.Application.Features.Community;

public sealed record AskKnowledgeAssistant(string Question);
public sealed record KnowledgeAssistantSource(int Number, KnowledgePassage Passage);
public sealed record KnowledgeAssistantContact(string Message, string CreateTicketUrl, string KnowledgeUrl);
public sealed record KnowledgeAssistantResponse(string Status, string Answer, IReadOnlyList<KnowledgeAssistantSource> Sources,
    KnowledgeAssistantContact Contact, string Method = "local-extractive-v1");
public sealed record KnowledgeSelection(bool Sufficient, IReadOnlyList<int> PassageIds);
public sealed record KnowledgeSourceVersion(Guid EntryId, int Revision);
public interface IKnowledgeCitationVerifier
{
    Task<bool> AreCurrentAsync(IReadOnlyList<KnowledgeSourceVersion> sources, CancellationToken ct);
}
public interface ILocalKnowledgeModel
{
    Task<KnowledgeSelection> SelectAsync(string question, IReadOnlyList<KnowledgePassage> passages, CancellationToken ct);
}
public enum KnowledgeModelFailure { Unavailable, Busy }
public sealed class KnowledgeModelException(KnowledgeModelFailure failure) : Exception("Local knowledge model could not complete the request.")
{
    public KnowledgeModelFailure Failure { get; } = failure;
}

public sealed class KnowledgeAssistantService(KnowledgeRetrievalService retrieval, ILocalKnowledgeModel model,
    IKnowledgeCitationVerifier citations, IOptions<LocalAssistantOptions> options)
{
    private static readonly KnowledgeAssistantContact Contact = new(
        "Nếu chưa giải quyết được, đăng nhập để tạo Ticket. Nếu không đăng nhập được, liên hệ đầu mối Helpdesk hoặc quản trị của đơn vị theo kênh đã được công bố.",
        "/login?returnTo=%2Ftickets%2Fnew", "/knowledge");

    public async Task<KnowledgeAssistantResponse> AskAsync(AskKnowledgeAssistant request, CancellationToken ct)
    {
        var question = KnowledgeRetrievalService.ValidateQuery(request.Question);
        if (!options.Value.Enabled) return Reply("Unavailable", "Trợ lý AI local chưa được bật. Bạn có thể tra cứu kho tri thức hoặc liên hệ hỗ trợ.");
        var result = await retrieval.SearchAsync(new(question, 3), ct);
        if (result.Items.Count == 0) return Reply("InsufficientInformation", "Chưa đủ thông tin trong tài liệu thực tế đã duyệt để trả lời câu hỏi này. Hãy mô tả rõ dịch vụ hoặc thao tác, hoặc liên hệ hỗ trợ.");
        if (result.CandidateLimitReached) return Reply("InsufficientInformation", "Câu hỏi khớp quá nhiều tài liệu. Chưa đủ thông tin để chọn hướng dẫn phù hợp; hãy thêm tên ứng dụng hoặc thao tác cụ thể.");
        KnowledgeSelection selection;
        try { selection = await model.SelectAsync(question, result.Items, ct); }
        catch (KnowledgeModelException error)
        {
            return error.Failure == KnowledgeModelFailure.Busy
                ? Reply("Busy", "Trợ lý AI local đang bận. Vui lòng thử lại sau hoặc tra cứu tài liệu và liên hệ hỗ trợ.")
                : Reply("Unavailable", "Trợ lý AI local hiện chưa sẵn sàng. Bạn có thể tra cứu kho tri thức hoặc liên hệ hỗ trợ.");
        }
        // A local model is still an untrusted selector. Never accept fabricated source IDs or free text.
        if (selection.PassageIds.Count > 3 || selection.PassageIds.Distinct().Count() != selection.PassageIds.Count ||
            selection.PassageIds.Any(id => id < 1 || id > result.Items.Count) || selection.Sufficient != (selection.PassageIds.Count > 0))
            return Reply("Unavailable", "Trợ lý chưa xác định được câu trả lời có nguồn hợp lệ. Hãy tra cứu tài liệu hoặc liên hệ hỗ trợ.");
        if (!selection.Sufficient) return Reply("InsufficientInformation", "Chưa đủ thông tin trong các đoạn tài liệu đã duyệt để trả lời chắc chắn. Vui lòng bổ sung ngữ cảnh hoặc liên hệ hỗ trợ.");
        var selected = selection.PassageIds.Select(id => result.Items[id - 1]).ToArray();
        var versions = selected.Select(p => new KnowledgeSourceVersion(p.EntryId, p.Revision)).Distinct().ToArray();
        if (!await citations.AreCurrentAsync(versions, ct))
            return Reply("SourcesChanged", "Tài liệu vừa thay đổi hoặc không còn được công khai. Vui lòng tra cứu lại hoặc liên hệ hỗ trợ.");
        var sources = selected.Select((passage, i) => new KnowledgeAssistantSource(i + 1, passage)).ToArray();
        var answer = "Các đoạn hướng dẫn đã được duyệt có liên quan dưới đây. Kiểm tra phạm vi áp dụng và đọc đầy đủ bài nguồn trước khi thực hiện.\n\n" +
            string.Join("\n\n", sources.Select(source => $"{source.Passage.Text} [{source.Number}]"));
        return new("Answered", answer, sources, Contact);
    }
    private static KnowledgeAssistantResponse Reply(string status, string message) => new(status, message, [], Contact);
}
