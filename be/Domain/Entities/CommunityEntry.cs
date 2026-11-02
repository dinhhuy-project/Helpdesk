namespace be.Domain.Entities;

public enum CommunityKind { Knowledge, Incident }
public enum IncidentStatus { Investigating, Identified, Monitoring, Resolved }
public enum CommunityClassification { Unclassified, Operational, Demo }

public sealed record KnowledgeGuide(string Applicability, string Prerequisites, string ExpectedOutcome, string Escalation,
    string SourceName, string? SourceUrl = null, DateOnly? SourceUpdatedOn = null);

// Draft and published snapshots are separate: editing never silently republishes content.
public class CommunityEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CommunityKind Kind { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Content { get; set; }
    public required string Topic { get; set; }
    public IncidentStatus? Status { get; set; }
    public Guid? SourceTicketId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicTitle { get; set; }
    public string? PublicSummary { get; set; }
    public string? PublicContent { get; set; }
    public string? PublicTopic { get; set; }
    public IncidentStatus? PublicStatus { get; set; }
    public int PublicRevision { get; set; }
    public DateTime? PublishedAt { get; set; }
    public uint Version { get; set; }
    public CommunityClassification Classification { get; set; }
    public CommunityClassification PublicClassification { get; set; }
    public KnowledgeGuide? Guide { get; set; }
    public KnowledgeGuide? PublicGuide { get; set; }
}

public class IncidentUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public required string Message { get; set; }
    public IncidentStatus Status { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IncidentAffectedUser
{
    public Guid EntryId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class KnowledgeFeedback
{
    public Guid EntryId { get; set; }
    public Guid UserId { get; set; }
    public bool Helpful { get; set; }
    public bool Solved { get; set; }
    public DateTime UpdatedAt { get; set; }
}
