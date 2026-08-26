using AngryMonkey.CloudLogin.Sever.Providers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

internal static class CloudLoginConfigurationProjection
{
    private static readonly HashSet<string> ExcludedCloudLoginProperties =
    [
        "Providers", "Cosmos", "AzureStorage", "WebConfig", "EmailSendCodeRequest"
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
                    resource.WithEnvironment(key, formatted);
                continue;
            }

            if (value is IEnumerable values)
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

                continue;
            }

            ProjectObject(resource, key, value, defaultValue);
        }
    }

    private static bool TryFormat(object value, out string? formatted)
    {
        Type type = value.GetType();

        if (value is Uri uri)
            formatted = uri.AbsoluteUri;
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
