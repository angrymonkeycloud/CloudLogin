namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// The session needed renewing and the authority could not be reached. The session is still valid: callers treat this like any other
/// network failure and retry, rather than signing the person out.
/// </summary>
public sealed class CloudLoginAuthorityUnavailableException(string message) : HttpRequestException(message, null, System.Net.HttpStatusCode.ServiceUnavailable);
