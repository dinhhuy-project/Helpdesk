namespace be.Application.Features.Community;

public enum CommunityError { InvalidInput, InvalidTransition, NotFound, Unauthenticated, InsufficientScope, VersionConflict, SourceDisabled, SourceUnavailable }
public sealed class CommunityOperationException(CommunityError error) : Exception("Community operation rejected.")
{
    public CommunityError Error { get; } = error;
}
