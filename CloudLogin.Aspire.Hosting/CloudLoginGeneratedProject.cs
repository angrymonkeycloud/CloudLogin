using Aspire.Hosting;
using CoconutSharp.Communications.Email;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Writes the CloudLogin server project an AppHost runs when the application has no login project
/// of its own, under the AppHost's intermediate output folder so nothing lands in the solution or
/// the source tree.
/// </summary>
/// <remarks>
/// CloudLogin is referenced from source when this package was itself taken as a project reference
/// and the checkout is still where it was built, and from NuGet at this package's own version
/// otherwise. The project is regenerated on every AppHost start; unchanged files are left alone so
/// its incremental build stays incremental.
/// </remarks>
internal static class CloudLoginGeneratedProject
{
    internal const string LaunchProfileName = "https";

    private const string IntermediateOutputPathKey = "apphostprojectbaseintermediateoutputpath";
    private static readonly Assembly HostingAssembly = typeof(CloudLoginGeneratedProject).Assembly;

    public static string Generate(IDistributedApplicationBuilder builder, string name, CloudLoginProjectOptions options)
    {
        string directory = Path.Combine(IntermediateOutputPath(builder), "cloudlogin", name);
        string projectPath = Path.Combine(directory, $"{name}.csproj");

        Directory.CreateDirectory(Path.Combine(directory, "Properties"));
        WriteIfChanged(projectPath, ProjectFile());
        WriteIfChanged(Path.Combine(directory, "Program.cs"), Template("Program.cs"));
        WriteIfChanged(Path.Combine(directory, "appsettings.json"), Template("appsettings.json"));
        WriteIfChanged(Path.Combine(directory, "Properties", "launchSettings.json"), LaunchSettings(options));
        MirrorWebRoot(builder, options.WebRootPath, Path.Combine(directory, "wwwroot"));

        return projectPath;
    }

    /// <summary>The AppHost's <c>obj</c> folder, as the Aspire SDK recorded it in the AppHost assembly.</summary>
    private static string IntermediateOutputPath(IDistributedApplicationBuilder builder)
    {
        string? recorded = Assembly.GetEntryAssembly()
            ?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, IntermediateOutputPathKey, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return string.IsNullOrWhiteSpace(recorded) ? Path.Combine(builder.AppHostDirectory, "obj") : recorded;
    }

    private static string ProjectFile()
    {
        XElement sdkProps = new("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"));
        XElement sdkTargets = new("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"));

        // Imported by hand rather than through the Sdk attribute so the isolation properties are
        // in place before the SDK reads them: the consuming tree's Directory.Build.props and
        // central package versions describe the application's own code, not this project.
        XDocument document = new(new XElement("Project",
            new XElement("PropertyGroup",
                new XElement("ImportDirectoryBuildProps", "false"),
                new XElement("ImportDirectoryBuildTargets", "false"),
                new XElement("ImportDirectoryPackagesProps", "false"),
                new XElement("ManagePackageVersionsCentrally", "false")),
            sdkProps,
            new XElement("PropertyGroup",
                new XElement("TargetFramework", TargetFramework()),
                new XElement("Nullable", "enable"),
                new XElement("ImplicitUsings", "enable"),
                new XElement("BlazorDisableThrowNavigationException", "true")),
            References(),
            sdkTargets));

        return document.ToString();
    }

    private static XElement References()
    {
        bool fromSource = HostingPackageIsProjectReference();
        string? cloudLoginRoot = fromSource ? Metadata("CloudLoginSourceRoot") : null;
        string? coconutSharpRoot = fromSource ? Metadata("CoconutSharpSourceRoot") : null;
        string cloudLoginVersion = PackageVersion(HostingAssembly);

        return new XElement("ItemGroup",
            Reference(cloudLoginRoot, "CloudLogin.Web", "AngryMonkey.CloudLogin.Web", cloudLoginVersion),
            Reference(cloudLoginRoot, "CloudLogin.Aspire", "AngryMonkey.CloudLogin.Aspire", cloudLoginVersion),
            Reference(coconutSharpRoot, "CoconutSharp.Communications", "CoconutSharp.Communications", PackageVersion(typeof(IEmailSender).Assembly)));
    }

    private static XElement Reference(string? sourceRoot, string project, string package, string version)
    {
        string? projectPath = sourceRoot is null ? null : Path.GetFullPath(Path.Combine(sourceRoot, project, $"{project}.csproj"));

        return projectPath is not null && File.Exists(projectPath)
            ? new XElement("ProjectReference", new XAttribute("Include", projectPath))
            : new XElement("PackageReference", new XAttribute("Include", package), new XAttribute("Version", version));
    }

    /// <summary>
    /// Whether the AppHost took this package as a project reference, read from its deps.json. The
    /// source root recorded at build time is only trusted in that case: on the machine the NuGet
    /// package was built on, the checkout exists for package consumers too.
    /// </summary>
    private static bool HostingPackageIsProjectReference()
    {
        if (AppContext.GetData("APP_CONTEXT_DEPS_FILES") is not string files)
            return false;

        string prefix = $"{HostingAssembly.GetName().Name}/";

        foreach (string file in files.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(file))
                continue;

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));

                if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries))
                    continue;

                foreach (JsonProperty library in libraries.EnumerateObject())
                {
                    if (library.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return library.Value.TryGetProperty("type", out JsonElement type) && type.ValueEquals("project");
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static string? Metadata(string key) =>
        HostingAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;

    private static string PackageVersion(Assembly assembly)
    {
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+', 2)[0];

        return (assembly.GetName().Version ?? new Version(1, 0, 0)).ToString(3);
    }

    private static string TargetFramework()
    {
        FrameworkName framework = new(HostingAssembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? ".NETCoreApp,Version=v10.0");

        return $"net{framework.Version.Major}.{framework.Version.Minor}";
    }

    private static string LaunchSettings(CloudLoginProjectOptions options) =>
        JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LaunchProfileName] = new
                {
                    commandName = "Project",
                    dotnetRunMessages = true,
                    launchBrowser = false,
                    applicationUrl = $"https://localhost:{options.HttpsPort};http://localhost:{options.HttpPort}",
                    environmentVariables = new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development" }
                }
            }
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string Template(string name)
    {
        string resourceName = HostingAssembly.GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith($".Template.{name}", StringComparison.Ordinal));

        using Stream stream = HostingAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded CloudLogin template '{resourceName}' was not found.");
        using StreamReader reader = new(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static void MirrorWebRoot(IDistributedApplicationBuilder builder, string? webRootPath, string target)
    {
        if (webRootPath is null)
        {
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            return;
        }

        string source = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, webRootPath));

        if (!Directory.Exists(source))
            throw new DistributedApplicationException($"The CloudLogin web root '{source}' does not exist.");

        Directory.CreateDirectory(target);
        HashSet<string> mirrored = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            mirrored.Add(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!File.Exists(destination) || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
                File.Copy(file, destination, overwrite: true);
        }

        foreach (string stale in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Where(file => !mirrored.Contains(file)).ToList())
            File.Delete(stale);
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            return;

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
