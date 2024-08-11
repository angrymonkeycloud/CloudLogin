using CloudLogin.Nuget;
using Microsoft.Extensions.Configuration;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appconfig.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>();

IConfigurationRoot configuration = builder.Build();
string apiKey = configuration["NuGetApiKey"];

await new NugetPacking(apiKey)
{
    MetadataProperies =
    [
        "PropertyGroup/Authors",
        "PropertyGroup/Company",
        "PropertyGroup/Version",
        "PropertyGroup/AssemblyVersion",
        "PropertyGroup/FileVersion",
        "PropertyGroup/PackageIcon"
    ],
    Projects =
    [
        new Project("CloudLogin.DataContract"),
        new Project("CloudLogin.Client"),
        new Project("CloudLogin.Server"),
        new Project("CloudLogin.Web.Components"){ PackAndPublish = false },
        new Project("CloudLogin.Web"),
    ]
}.Pack();