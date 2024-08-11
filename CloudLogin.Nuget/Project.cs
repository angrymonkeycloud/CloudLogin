using System.Xml.Linq;

namespace CloudLogin.Nuget;

public class Project
{
    public Project(string name)
    {
        Name = name;
        Document = XDocument.Load(FilePath, LoadOptions.PreserveWhitespace);
        AssemblyName = NugetPacking.GetProjectPropertyValue(Document, "PropertyGroup/AssemblyName")!;
    }

    public string Name { get; init; }
    public string FilePath => $"../../../../{Name}/{Name}.csproj";
    public bool UpdateMetadata { get; init; } = true;
    public bool PackAndPublish { get; init; } = true;

    internal string AssemblyName { get; set; }
    internal XDocument Document { get; set; }
}
