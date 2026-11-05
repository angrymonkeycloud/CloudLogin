namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginServerConfiguration
{
    public string? LoginUrl { get; set; }
    public string CookieName { get; set; } = "__Host-CloudLogin.Consumer";
    public string? CookieDomain { get; set; }
    public TimeSpan SessionDuration { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// How long this application's session lasts when the person chose to stay signed in: a cookie that survives closing the
    /// browser, renewed while they keep using it. The authority's session still bounds it (<c>SessionFamilyLifetime</c>, 30 days
    /// by default), and signing out anywhere still ends it.
    /// </summary>
    public TimeSpan KeepSignedInDuration { get; set; } = TimeSpan.FromDays(30);
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Rejects consumer cookies that carry no authority session binding, such as ones issued before sessions were bound.
    /// Without the binding a cookie cannot be ended by a back-channel logout, so it is refused rather than trusted.
    /// </summary>
    public bool RequireSessionBinding { get; set; } = true;

    /// <summary>
    /// The custom URL schemes of native applications that sign in through this application (for example <c>blusky</c>). Empty means no native
    /// application may use the <c>auth/native</c> endpoints. Nothing else needs configuring: the native flow reuses this application's own
    /// CloudLogin identity and callback.
    /// </summary>
    public List<string> NativeCallbackSchemes { get; set; } = [];
}
