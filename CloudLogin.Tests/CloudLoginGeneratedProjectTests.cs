using AngryMonkey.CloudLogin.Aspire.Hosting;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AngryMonkey.CloudLogin.Tests;

public sealed class CloudLoginGeneratedProjectTests
{
    private static IDistributedApplicationBuilder NewBuilder(params string[] args) =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = args, DisableDashboard = true });

    [Fact]
    public void TheProject_IsGeneratedUnderTheAppHostObjFolder_NamedAfterTheResource()
    {
        IDistributedApplicationBuilder builder = NewBuilder();

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject("generated-login");

        string projectPath = login.Resource.GetProjectMetadata().ProjectPath;
        string expected = Path.Combine(builder.AppHostDirectory, "obj", "cloudlogin", "generated-login", "generated-login.csproj");

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(projectPath));
        Assert.True(File.Exists(projectPath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "appsettings.json")));
    }

    [Fact]
    public void TheDefaultName_IsLogin_AndTheConfigurationCallbackIsApplied()
    {
        IDistributedApplicationBuilder builder = NewBuilder();

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject(configuration => configuration.Title = "Generated");

        Assert.Equal("login", login.Resource.Name);
        Assert.Single(login.Resource.Annotations.OfType<CloudLoginServerAnnotation>());
    }

    [Fact]
    public void TheLaunchProfile_CarriesTheConfiguredPorts_AsTheResourceEndpoints()
    {
        IDistributedApplicationBuilder builder = NewBuilder();

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject("ported-login", configureProject: project =>
        {
            project.HttpsPort = 7116;
            project.HttpPort = 5045;
        });

        List<EndpointAnnotation> endpoints = [.. login.Resource.Annotations.OfType<EndpointAnnotation>()];

        Assert.Equal(7116, Assert.Single(endpoints, endpoint => endpoint.Name == "https").Port);
        Assert.Equal(5045, Assert.Single(endpoints, endpoint => endpoint.Name == "http").Port);
        Assert.All(endpoints, endpoint => Assert.True(endpoint.IsExternal));
    }

    [Fact]
    public void InRunMode_TheProjectIsLaunchedAsAPlainDotnetRun_ThatBuildsIt()
    {
        // Visual Studio launches project resources through the IDE, which only knows projects in the
        // open solution; the override keeps DCP launching this one itself, and a `dotnet run`
        // without --no-build is what compiles a project nothing else references.
        IDistributedApplicationBuilder builder = NewBuilder();

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject("process-login");

#pragma warning disable ASPIREPROJECTS001
        ProjectLaunchArgsOverrideAnnotation launch = Assert.Single(login.Resource.Annotations.OfType<ProjectLaunchArgsOverrideAnnotation>());
        Assert.Equal(["run", "--no-launch-profile", "--project"], launch.Arguments);
#pragma warning restore ASPIREPROJECTS001
    }

    [Fact]
    public void InPublishMode_NoLaunchOverrideIsAdded()
    {
        IDistributedApplicationBuilder builder = NewBuilder("--operation", "publish");

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject("published-login");

#pragma warning disable ASPIREPROJECTS001
        Assert.Empty(login.Resource.Annotations.OfType<ProjectLaunchArgsOverrideAnnotation>());
#pragma warning restore ASPIREPROJECTS001
        Assert.True(File.Exists(login.Resource.GetProjectMetadata().ProjectPath));
    }

    [Fact]
    public void WhenThisPackageIsAProjectReference_CloudLoginIsReferencedFromSource()
    {
        // This test project takes the hosting package as a project reference, so the generated
        // project must build against the same checkout rather than a published package.
        IDistributedApplicationBuilder builder = NewBuilder();

        ICloudLoginServerBuilder login = builder.AddCloudLoginProject("source-login");
        string project = File.ReadAllText(login.Resource.GetProjectMetadata().ProjectPath);

        Assert.Contains("CloudLogin.Web.csproj", project);
        Assert.Contains("CloudLogin.Aspire.csproj", project);
        Assert.DoesNotContain("PackageReference Include=\"AngryMonkey.CloudLogin", project);
        Assert.Contains("<ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>", project);
    }

    [Fact]
    public void TheWebRoot_IsMirroredIntoTheGeneratedProject_AndDroppedWhenNoLongerConfigured()
    {
        string webRoot = Path.Combine(Path.GetTempPath(), "cloudlogin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "logo.svg"), "<svg />");

        try
        {
            ICloudLoginServerBuilder login = NewBuilder().AddCloudLoginProject("branded-login", configureProject: project => project.WebRootPath = webRoot);
            string generatedLogo = Path.Combine(Path.GetDirectoryName(login.Resource.GetProjectMetadata().ProjectPath)!, "wwwroot", "logo.svg");

            Assert.Equal("<svg />", File.ReadAllText(generatedLogo));

            NewBuilder().AddCloudLoginProject("branded-login");

            Assert.False(File.Exists(generatedLogo));
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public void AMissingWebRoot_IsReportedRatherThanIgnored()
    {
        IDistributedApplicationBuilder builder = NewBuilder();

        Assert.Throws<DistributedApplicationException>(() =>
            builder.AddCloudLoginProject("missing-root-login", configureProject: project => project.WebRootPath = "does-not-exist"));
    }
}
