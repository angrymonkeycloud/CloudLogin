using System.Diagnostics;
using System.Xml.Linq;
using System.Xml;

namespace CloudLogin.Nuget;

public class InternalMethods(string apiKey)
{
    readonly string ApiKey = apiKey;

    public string[] MetadataProperies { get; set; } = [];
    public Project[] Projects { get; set; } = [];

    internal string? GetProjectPropertyValue(XDocument doc, string propertyName)
    {
        XElement element = doc.Root ?? throw new Exception("Project document issue");

        foreach (string nodeName in propertyName.Split('/'))
        {
            XElement? nextElement = element.Element(nodeName);

            if (nextElement == null)
                return null;

            element = nextElement;
        }

        return element.Value;
    }

    internal async Task UpdateProjectMetadata(Project project)
    {
        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true
        };

        using XmlWriter writer = XmlWriter.Create(project.FilePath, settings);
        await Task.Run(() => project.Document.Save(writer));

        Console.WriteLine($"Updated {project.Name}");
    }

    internal void UpdateProjectNode(Project project, string propertyName, string value)
    {
        XElement element = project.Document.Root ?? throw new Exception("Project document issue");

        foreach (string nodeName in propertyName.Split('/'))
        {
            XElement? nextElement = element.Element(nodeName);

            if (nextElement == null)
            {
                nextElement = new XElement(nodeName);
                element.Add(nextElement);
            }

            element = nextElement;
        }

        element.Value = value;
    }

    internal async Task PackProject(string project)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"pack {project} -c Release -o ./nupkgs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode == 0)
            Console.WriteLine($"Successfully packed {project}");
        else
            Console.WriteLine($"Error packing {project}: {error}");
    }

    internal async Task PublishPackage(string project)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"nuget push ./nupkgs/{project}.*.nupkg -k {ApiKey} -s https://api.nuget.org/v3/index.json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode == 0)
            Console.WriteLine($"Successfully published {project}");
        else
            Console.WriteLine($"Error publishing {project}: {error}");
    }

    public async Task Pack()
    {
        // Update Metadata

        foreach (string metadata in MetadataProperies)
        {
            Project sourceProject = new("CloudLogin.Nuget");

            string value = GetProjectPropertyValue(sourceProject.Document, metadata) ?? throw new Exception("Metadata not foud at source");

            foreach (Project project in Projects.Where(key => key.UpdateMetadata))
            {
                UpdateProjectNode(project, metadata, value);
                await UpdateProjectMetadata(project);
            }
        }

        // Pack and Publish
        //foreach (Project? project in Projects.Where(key => key.PackAndPublish))
        //{
        //    await PackProject(project.Name);
        //    await PublishPackage(project.Name);
        //}
    }
}
