using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AngryMonkey.CloudLogin.Server.Versioning;

/// <summary>
/// Serves the latest enabled API version at unversioned routes.
/// <para>
/// Versioned paths always work — <c>/api/v3/users/me</c> stays valid so an integration can pin
/// a version explicitly. On top of that, the controllers of the <em>latest enabled</em> version
/// also answer with the version segment stripped: while V3 is the newest, <c>/api/users/me</c>
/// serves V3; when a V4 arrives and is enabled, the unversioned surface moves to V4 and V3
/// remains reachable only at <c>/api/v3</c>. Older façades keep their own routes — V2's
/// unversioned legacy paths (<c>/CloudLogin/...</c>) are its contract, not an alias.
/// </para>
/// </summary>
public sealed class SelectedApiVersionRouteConvention(CloudLoginApiVersion apiVersion) : IControllerModelConvention
{
    private readonly CloudLoginApiVersion _apiVersion = apiVersion;

    public void Apply(ControllerModel controller)
    {
        ApiVersionGateAttribute? gate = controller.Attributes.OfType<ApiVersionGateAttribute>().FirstOrDefault();
        if (gate is null)
            return;

        if (gate.Version != _apiVersion)
            return;

        string versionSegment = $"v{(int)gate.Version}";

        foreach (SelectorModel selector in controller.Selectors.ToList())
        {
            string? template = selector.AttributeRouteModel?.Template;
            if (template is null)
                continue;

            string? unversioned = StripVersionSegment(template, versionSegment);
            if (unversioned is null)
                continue;

            controller.Selectors.Add(new SelectorModel(selector)
            {
                AttributeRouteModel = new AttributeRouteModel { Template = unversioned }
            });
        }
    }

    /// <summary>Removes one <c>/v{n}/</c> path segment; null when the template has none.</summary>
    public static string? StripVersionSegment(string template, string versionSegment)
    {
        string[] segments = template.Split('/');
        int index = Array.FindIndex(segments, segment =>
            string.Equals(segment, versionSegment, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
            return null;

        return string.Join('/', segments.Where((_, position) => position != index));
    }
}
