using System.Reflection;
using System.Text.RegularExpressions;

namespace WorkshopUI;

public static class WorkshopGuides
{
    public static IReadOnlyDictionary<string, string> Load(Assembly assembly) =>
        assembly.GetManifestResourceNames().Where(name => name.StartsWith("WorkshopGuide.", StringComparison.Ordinal))
            .ToDictionary(name => name["WorkshopGuide.".Length..].Replace(".md", "", StringComparison.Ordinal).ToLowerInvariant(),
                name => Read(assembly, name), StringComparer.OrdinalIgnoreCase);

    private static string Read(Assembly assembly, string name)
    {
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public static WorkshopFeature Feature(string id, string markdown)
    {
        string title = markdown.Split('\n').FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? id;
        string fence = new((char)96, 3);
        MatchCollection blocks = Regex.Matches(markdown, fence + "[^\\r\\n]*\\r?\\n(.*?)" + fence, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        string code = string.Join("\n\n", blocks.Select(match => match.Groups[1].Value.Trim()));
        return new("guide-" + id, title, "Reference guides", "Configuration, integration, and operational guidance bundled with this workshop.",
            string.IsNullOrWhiteSpace(code) ? "// This reference has no code sample. Open Instructions for the full guide." : code,
            ["Read the guide below, then use the related live examples in the left navigation.", "Copy the relevant example from Code and adapt resource names to your application."]);
    }
}
