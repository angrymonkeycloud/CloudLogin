using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Tokens;

public interface ICloudLoginClientCredentials
{
    string? ClientId { get; }

    bool IsConfigured { get; }

    Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A website's backend proves itself to the authority with its name and a secret key (<c>CloudLogin:ClientId</c>,
/// <c>CloudLogin:ClientSecret</c>). Any valid key works; several websites may share one.
/// </summary>
public sealed class CloudLoginClientCredentials(IOptions<CloudLoginTokenClientOptions> options) : ICloudLoginClientCredentials
{
    private readonly CloudLoginTokenClientOptions _options = options.Value;

    public string? ClientId => _options.ClientId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("This application has no CloudLogin secret key configured (CloudLogin:ClientSecret).");

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
        return Task.CompletedTask;
    }
}
