namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>How an application that references CloudLogin signs in. The default is a website with a backend, using the AppHost's shared secret key.</summary>
public sealed class CloudLoginClientOptions
{
    /// <summary>
    /// A website without a backend, or a native app: it gets no secret key and is identified by its own address, so the tokens it receives
    /// are only valid there. It needs an endpoint to return to.
    /// </summary>
    public bool IsPublic { get; set; }
}
