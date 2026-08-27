using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// The builder <c>AddCloudLogin</c> returns: an ordinary Aspire project builder, distinguished
/// only by its type.
/// </summary>
/// <remarks>
/// <para>
/// Every Aspire API keeps working on it - it is an <see cref="IResourceBuilder{T}"/> over the same
/// <see cref="ProjectResource"/> - and the resource in the application model is a plain project, so
/// nothing downstream (deployment tools included) sees anything unusual.
/// </para>
/// <para>
/// The type exists so this package can offer <c>WithReference(cosmos)</c> and
/// <c>WithReference(storage)</c> without colliding with any other component package that does the
/// same. Two extension methods with the same signature are ambiguous to the C# compiler no matter
/// which namespaces they live in; constraining each package's overloads to its own builder type
/// makes the other package's candidates fail their constraint and drop out, which is what lets one
/// AppHost use both.
/// </para>
/// <para>
/// Hold <c>AddCloudLogin</c>'s result in a <c>var</c>. Typing the variable as
/// <see cref="IResourceBuilder{T}"/> of <see cref="ProjectResource"/> hides these overloads, and a
/// call that would have written CloudLogin's own configuration keys silently binds to Aspire's
/// generic <c>WithReference</c> instead - which compiles, sets only <c>ConnectionStrings__{name}</c>,
/// and leaves CloudLogin reading nothing. Every overload here returns the caller's own builder type,
/// so references chain in any order; only stock Aspire methods (<c>WaitFor</c>,
/// <c>WithEnvironment</c>) erase it, so put them after the references rather than between them.
/// </para>
/// </remarks>
public interface ICloudLoginServerBuilder : IResourceBuilder<ProjectResource>;

/// <summary>Delegates every member to the project builder Aspire created.</summary>
internal sealed class CloudLoginServerBuilder(IResourceBuilder<ProjectResource> inner) : ICloudLoginServerBuilder
{
    public IDistributedApplicationBuilder ApplicationBuilder => inner.ApplicationBuilder;

    public ProjectResource Resource => inner.Resource;

    public IResourceBuilder<ProjectResource> WithAnnotation<TAnnotation>(
        ResourceAnnotationMutationBehavior behavior = ResourceAnnotationMutationBehavior.Append)
        where TAnnotation : IResourceAnnotation, new() =>
        inner.WithAnnotation<TAnnotation>(behavior);

    public IResourceBuilder<ProjectResource> WithAnnotation<TAnnotation>(
        TAnnotation annotation,
        ResourceAnnotationMutationBehavior behavior = ResourceAnnotationMutationBehavior.Append)
        where TAnnotation : IResourceAnnotation =>
        inner.WithAnnotation(annotation, behavior);
}
