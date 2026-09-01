namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// A create-only write found an existing record: an identity key already mapped to another user,
/// a duplicate document id, or a lost bootstrap reservation. Callers decide whether that means
/// "already done" (idempotent retry) or a real conflict.
/// </summary>
public class CoreConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// An optimistic-concurrency (ETag) precondition failed: someone else changed the record between
/// read and write. Callers re-read and re-apply, or surface the conflict.
/// </summary>
public class CoreConcurrencyException(string message) : InvalidOperationException(message);
