using System.Diagnostics;

namespace AngryMonkey.CloudLogin.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int Usage = 2;
    public const int Unavailable = 3;
}

/// <summary>Everything the CLI touches outside itself, so the commands run unchanged against a test host.</summary>
public sealed class CliEnvironment
{
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public required TextReader In { get; init; }

    /// <summary>The innermost handler for every request. It must not keep cookies or follow redirects; the CLI does both itself.</summary>
    public required Func<HttpMessageHandler> CreateTransport { get; init; }

    public required ISessionProtector Protector { get; init; }
    public required string DefaultSessionPath { get; init; }
    public Action<string>? OpenBrowser { get; init; }
    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static CliEnvironment CreateDefault() => new()
    {
        Out = Console.Out,
        Error = Console.Error,
        In = Console.In,
        CreateTransport = () => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
        Protector = SessionProtectors.ForCurrentPlatform(),
        DefaultSessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AngryMonkey", "CloudLogin.Cli", "session.json"),
        OpenBrowser = url =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    };
}
