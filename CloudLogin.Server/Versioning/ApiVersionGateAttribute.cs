using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Server.Versioning;

/// <summary>
/// Gates a controller (or action) behind an API façade version: when the deployment has not
/// enabled that version, the endpoint answers 404 exactly as if it did not exist. Absence of the
/// attribute means the endpoint is version-neutral (health, static assets, UI pages).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiVersionGateAttribute(CloudLoginApiVersion version) : Attribute, IResourceFilter
{
    public CloudLoginApiVersion Version { get; } = version;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        CloudLoginWebConfiguration? configuration =
            context.HttpContext.RequestServices.GetService<CloudLoginWebConfiguration>();

        if (configuration is not null && configuration.ApiVersion != Version)
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
