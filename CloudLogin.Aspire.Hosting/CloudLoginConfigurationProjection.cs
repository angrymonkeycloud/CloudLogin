using AngryMonkey.CloudLogin.Sever.Providers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

internal static class CloudLoginConfigurationProjection
{
    private static readonly HashSet<string> ExcludedCloudLoginProperties =
    [
        "Providers", "Cosmos", "AzureStorage", "WebConfig", "EmailSendCodeRequest",

        // Secrets never travel through the reflection projection, which writes literal values
        // into the resource's environment and the published manifest. They get their own
        // parameter-based path instead - see WithIdentityHmacSecret and
        // WithIdentityHmacFallbackSecrets.
        "IdentityHmacSecret", "IdentityHmacFallbackSecrets"
    ];

    public static void Apply(
        IResourceBuilder<ProjectResource> cloudLogin,
        AngryMonkey.CloudLogin.Server.CloudLoginWebConfiguration configuration)
    {
        AngryMonkey.CloudLogin.Server.CloudLoginWebConfiguration defaults = new();

        ProjectObject(
            cloudLogin,
            "CloudLogin",
            configuration,
            defaults,
            ExcludedCloudLoginProperties);

        ProjectObject(cloudLogin, "Cosmos", configuration.Cosmos, defaults.Cosmos);

        if (configuration.AzureStorage is not null)
            ProjectObject(cloudLogin, "Storage", configuration.AzureStorage, defaults.AzureStorage);

        foreach (ProviderConfiguration provider in configuration.Providers)
            ProjectObject(cloudLogin, ProviderSection(provider), provider, null);
    }

    private static string ProviderSection(ProviderConfiguration provider) =>
        provider.Code.ToLowerInvariant() switch
        {
            "password" => "Password",
            "code" => "Code",
            "microsoft" => "Microsoft",
            "google" => "Google",
            "facebook" or "facbook" => "Facebook",
            "twitter" => "Twitter",
            "whatsapp" => "WhatsApp",
            "testmode" => "TestMode",
            _ => provider.Code
        };

    private static void ProjectObject(
        IResourceBuilder<ProjectResource> resource,
        string prefix,
        object current,
        object? defaults,
        IReadOnlySet<string>? excluded = null)
    {
        foreach (PropertyInfo property in current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || excluded?.Contains(property.Name) is true)
                continue;

            object? value = property.GetValue(current);

            if (value is null || value is Delegate || property.Name == "Credential")
                continue;

            object? defaultValue = defaults is null ? null : property.GetValue(defaults);
            string key = $"{prefix}:{property.Name}";

            if (TryFormat(value, out string? formatted))
            {
                if (!Equals(value, defaultValue))
                {
                    if (property.Name is "ClientSecret" or "Secret" or "Password" or "Authorization" or "ConnectionString" or "CertificatePassword" or "CertificateBase64")
                    {
                        string name = System.Text.RegularExpressions.Regex.Replace($"{resource.Resource.Name}-{key}".ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                        IResourceBuilder<ParameterResource> parameter = resource.ApplicationBuilder.AddParameter(name, () => formatted!, secret: true);
                        resource.WithEnvironment(key, parameter);
                    }
                    else
                        resource.WithEnvironment(key, formatted);
                }
                continue;
            }

            if (value is IEnumerable values)
            {
                // A collection identical to the type's own defaults must not be projected: the
                // service side seeds those same defaults and the configuration binder APPENDS
                // list items rather than replacing them, so re-sending defaults duplicates them
                // (two "default" sign-in profiles, doubled enabled versions, ...).
                if (StructurallyEqualsDefault(value, defaultValue))
                    continue;

                if (value is IDictionary dictionary)
                {
                    // Dictionaries bind by key, not by index.
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Value is null)
                            continue;

                        string entryKey = $"{key}:{entry.Key}";
                        if (TryFormat(entry.Value, out string? entryValue))
                            resource.WithEnvironment(entryKey, entryValue);
                        else if (entry.Value is IEnumerable entryItems && entry.Value is not string)
                            ProjectItems(resource, entryKey, entryItems);
                        else
                            ProjectObject(resource, entryKey, entry.Value, null);
                    }

                    continue;
                }

                ProjectItems(resource, key, values);
                continue;
            }

            ProjectObject(resource, key, value, defaultValue);
        }
    }

    private static void ProjectItems(IResourceBuilder<ProjectResource> resource, string key, IEnumerable values)
    {
        int index = 0;
        foreach (object? item in values)
        {
            if (item is null)
                continue;

            string itemKey = $"{key}:{index++}";
            if (TryFormat(item, out string? itemValue))
                resource.WithEnvironment(itemKey, itemValue);
            else
                ProjectObject(resource, itemKey, item, null);
        }
    }

    /// <summary>JSON-shape comparison; unserializable values are treated as different (projected).</summary>
    private static bool StructurallyEqualsDefault(object value, object? defaultValue)
    {
        if (defaultValue is null)
            return false;

        try
        {
            return JsonSerializer.Serialize(value) == JsonSerializer.Serialize(defaultValue);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFormat(object value, out string? formatted)
    {
        Type type = value.GetType();

        if (value is Uri uri)
            formatted = uri.AbsoluteUri;
        // Handled before the IFormattable branch below, because System.Boolean does not implement
        // IFormattable - unlike every other primitive. Without this every bool falls through
        // unformatted and is dropped from the projection in silence, so an AppHost can set one and
        // the service never sees it (this is what stopped TestMode:IsEnabled from ever arriving).
        else if (value is bool boolean)
            formatted = boolean ? "true" : "false";
        else if (value is TimeSpan timeSpan)
            formatted = timeSpan.ToString("c", CultureInfo.InvariantCulture);
        else if (value is IFormattable formattable &&
            (type.IsPrimitive || type.IsEnum || value is decimal || value is DateTime ||
             value is DateTimeOffset || value is Guid))
            formatted = formattable.ToString(null, CultureInfo.InvariantCulture);
        else if (value is string text)
            formatted = text;
        else
            formatted = null;

        return formatted is not null;
    }
}
