using System.Text.Json;
using be.Application.Features.Community;
using be.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class KnowledgeAssistantServiceTests
{
    [Fact]
    public async Task AnswerIsBuiltFromVerbatimEvidenceAndServerGeneratedCitations()
    {
        var (service, store, model, verifier) = Create();
        var answer = await service.AskAsync(new("VPN"), default);
        Assert.Equal("Answered", answer.Status);
        var source = Assert.Single(answer.Sources);
        Assert.Equal(1, source.Number);
        Assert.Contains(source.Passage.Text + " [1]", answer.Answer);
        Assert.Equal($"/knowledge/{source.Passage.EntryId}", source.Passage.SourceUrl);
        Assert.Equal("/login?returnTo=%2Ftickets%2Fnew", answer.Contact.CreateTicketUrl);
        Assert.Equal("/knowledge", answer.Contact.KnowledgeUrl);
        Assert.Equal(1, store.Calls); Assert.Equal(1, model.Calls); Assert.Equal(1, verifier.Calls);
    }
    [Fact]
    public async Task MissingDocumentsOrGenericQuestionsNeverInvokeTheModel()
    {
        var (service, store, model, verifier) = Create();
        store.Empty = true;
        Assert.Equal("InsufficientInformation", (await service.AskAsync(new("VPN"), default)).Status);
        store.Empty = false;
        Assert.Equal("InsufficientInformation", (await service.AskAsync(new("lam sao vay"), default)).Status);
        Assert.Equal(0, model.Calls); Assert.Equal(0, verifier.Calls);
    }
    [Fact]
    public async Task DisabledFeatureDoesNotDependOnDatabaseOrOllama()
    {
        var (service, store, model, _) = Create(false);
        Assert.Equal("Unavailable", (await service.AskAsync(new("VPN"), default)).Status);
        Assert.Equal(0, store.Calls); Assert.Equal(0, model.Calls);
        await Assert.ThrowsAsync<CommunityOperationException>(() => service.AskAsync(new("x"), default));
    }
    [Theory]
    [InlineData(true, "[]")]
    [InlineData(false, "[1]")]
    [InlineData(true, "[99]")]
    [InlineData(true, "[1,1]")]
    [InlineData(true, "[0]")]
    [InlineData(true, "[-1]")]
    [InlineData(true, "[1,2,3,4]")]
    public async Task InvalidModelSelectionsCannotCreateOrInventCitations(bool sufficient, string ids)
    {
        var (service, _, model, verifier) = Create();
        model.Selection = new(sufficient, JsonSerializer.Deserialize<int[]>(ids)!);
        var answer = await service.AskAsync(new("VPN"), default);
        Assert.Equal("Unavailable", answer.Status); Assert.Empty(answer.Sources); Assert.Equal(0, verifier.Calls);
    }
    [Fact]
    public async Task ModelCanAbstainEvenWhenKeywordsRetrievedSomeDocuments()
    {
        var (service, _, model, verifier) = Create(); model.Selection = new(false, []);
        var answer = await service.AskAsync(new("VPN"), default);
        Assert.Equal("InsufficientInformation", answer.Status);
        Assert.Empty(answer.Sources); Assert.Equal(0, verifier.Calls);
        Assert.Contains("liên hệ", answer.Answer);
    }
    [Fact]
    public async Task SourceFreshnessIsCheckedAfterGenerationAndOldTextIsNotReturned()
    {
        var (service, _, model, verifier) = Create();
        model.OnSelect = (_, _, _) => { verifier.Current = false; return Task.FromResult(new KnowledgeSelection(true, [1])); };
        var answer = await service.AskAsync(new("VPN"), default);
        Assert.Equal("SourcesChanged", answer.Status); Assert.Empty(answer.Sources);
        Assert.DoesNotContain("VPN client", answer.Answer); Assert.Equal(1, verifier.Calls);
    }
    [Theory]
    [InlineData(KnowledgeModelFailure.Busy, "Busy")]
    [InlineData(KnowledgeModelFailure.Unavailable, "Unavailable")]
    public async Task ModelFailuresReturnExplicitDegradedStateAndSupportGuidance(KnowledgeModelFailure failure, string expected)
    {
        var (service, _, model, _) = Create();
        model.OnSelect = (_, _, _) => throw new KnowledgeModelException(failure);
        var answer = await service.AskAsync(new("VPN"), default);
        Assert.Equal(expected, answer.Status); Assert.Empty(answer.Sources); Assert.Contains("Helpdesk", answer.Contact.Message);
    }
    [Fact]
    public async Task CallerCancellationIsNotRewrittenAsAModelOutage()
    {
        var (service, _, model, _) = Create(); using var source = new CancellationTokenSource();
        model.OnSelect = (_, _, ct) => { source.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(new KnowledgeSelection(true, [1])); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AskAsync(new("VPN"), source.Token));
    }

    private static (KnowledgeAssistantService, CandidateStore, AssistantTestModel, Verifier) Create(bool enabled = true)
    {
        var store = new CandidateStore(); var model = new AssistantTestModel(); var verifier = new Verifier();
        return (new(new KnowledgeRetrievalService(store), model, verifier, Options.Create(new LocalAssistantOptions { Enabled = enabled })), store, model, verifier);
    }
    private sealed class CandidateStore : IKnowledgeRetrievalStore
    {
        public int Calls { get; private set; }
        public bool Empty { get; set; }
        public Task<IReadOnlyList<KnowledgeCandidate>> CandidatesAsync(string query, CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<KnowledgeCandidate> rows = Empty ? [] : [new(Guid.NewGuid(), "VPN guide", "VPN", "1. Open VPN client.\n2. Reconnect VPN.", "Network", 1,
                DateTime.UtcNow, JsonSerializer.Serialize(CommunityTests.Guide(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))];
            return Task.FromResult(rows);
        }
    }
    private sealed class Verifier : IKnowledgeCitationVerifier
    {
        public int Calls { get; private set; }
        public bool Current { get; set; } = true;
        public Task<bool> AreCurrentAsync(IReadOnlyList<KnowledgeSourceVersion> sources, CancellationToken ct) { Calls++; return Task.FromResult(Current); }
    }
}

internal sealed class AssistantTestModel : ILocalKnowledgeModel
{
    public int Calls { get; private set; }
    public KnowledgeSelection Selection { get; set; } = new(true, [1]);
    public Func<string, IReadOnlyList<KnowledgePassage>, CancellationToken, Task<KnowledgeSelection>>? OnSelect { get; set; }
    public Task<KnowledgeSelection> SelectAsync(string question, IReadOnlyList<KnowledgePassage> passages, CancellationToken ct)
    {
        Calls++; return OnSelect?.Invoke(question, passages, ct) ?? Task.FromResult(Selection);
    }
}
