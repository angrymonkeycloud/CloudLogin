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
/// which namespaces they live in, so an AppHost using CloudLogin alongside another such component
/// could not call either. Hanging each package's overloads off its own builder type is what keeps
/// them independent - and is why <c>AddCloudLogin</c>'s result should be held in a <c>var</c>:
/// typing the variable as <see cref="IResourceBuilder{T}"/> of <see cref="ProjectResource"/> hides
/// these overloads again.
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
