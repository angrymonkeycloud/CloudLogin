namespace AngryMonkey.CloudLogin.Server.Core.Application;

public enum AdminErrorKinds
{
    NotFound,
    Conflict,
    Invalid,
    Forbidden
}

/// <summary>
/// A control-plane operation that cannot proceed, with a message safe to show an administrator.
/// Controllers map the kind to a status code; nothing else decides what a failure means.
/// </summary>
public sealed class AdminException(AdminErrorKinds kind, string message) : Exception(message)
{
    public AdminErrorKinds Kind { get; } = kind;

    public static AdminException NotFound(string message) => new(AdminErrorKinds.NotFound, message);
    public static AdminException Conflict(string message) => new(AdminErrorKinds.Conflict, message);
    public static AdminException Invalid(string message) => new(AdminErrorKinds.Invalid, message);
    public static AdminException Forbidden(string message) => new(AdminErrorKinds.Forbidden, message);
}
