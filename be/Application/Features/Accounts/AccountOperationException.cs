namespace be.Application.Features.Accounts;

public enum AccountError
{
    NotFound, VersionConflict, EmailInUse, InvalidDepartment, LastActiveAdmin, InvalidRole,
    InsufficientScope, ProtectedAdminGrant, InvalidCurrentPassword, PasswordlessAccount, Unauthenticated, InvalidInput
}

public sealed class AccountOperationException(AccountError error) : Exception
{
    public AccountError Error { get; } = error;
}
