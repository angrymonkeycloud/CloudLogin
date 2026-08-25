using System.Reflection;
using System.Security;
using System.Text;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

internal static class CloudLoginStandaloneProject
{
    public static string Extract()
    {
        Assembly assembly = typeof(CloudLoginStandaloneProject).Assembly;
        string version = assembly.GetName().Version?.ToString() ?? "dev";
        string directory = Path.Combine(
            Path.GetTempPath(),
            "angrymonkey-cloudlogin",
            version);
        Directory.CreateDirectory(directory);

        string sourceRoot = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                attribute.Key == "CloudLoginSourceRoot")
            ?.Value ?? string.Empty;

        if (!File.Exists(Path.Combine(
                sourceRoot,
                "CloudLogin.Web",
                "CloudLogin.Web.csproj")))
        {
            sourceRoot = string.Empty;
        }

        string escapedSourceRoot =
            SecurityElement.Escape(sourceRoot) ?? string.Empty;

        ExtractResource(
            assembly,
            "CloudLogin.Standalone.csproj",
            Path.Combine(directory, "CloudLogin.Standalone.csproj"),
            content => content.Replace(
                "__CLOUDLOGIN_SOURCE_ROOT__",
                escapedSourceRoot,
                StringComparison.Ordinal));

        ExtractResource(
            assembly,
            "Program.cs",
            Path.Combine(directory, "Program.cs"));

        return Path.Combine(directory, "CloudLogin.Standalone.csproj");
    }

    private static void ExtractResource(
        Assembly assembly,
        string suffix,
        string target,
        Func<string, string>? transform = null)
    {
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(
                $".Standalone.{suffix}",
                StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded CloudLogin host resource '{resourceName}' was not found.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        string content = reader.ReadToEnd();

        if (transform is not null)
            content = transform(content);

        if (!File.Exists(target)
            || !string.Equals(
                File.ReadAllText(target),
                content,
                StringComparison.Ordinal))
        {
            File.WriteAllText(
                target,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
