#pragma warning disable BL0006

using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The login page settles on its input step once its providers have loaded, so that is the step that has to offer
/// "Keep me signed in": a person signing in with a provider leaves the page from there.
/// </summary>
public class LoginComponentKeepSignedInTests
{
    private static CloudLoginProviderDefinition External(string code) => new(code)
    {
        HandlesEmailAddress = true,
        HandlesPhoneNumber = false,
        IsCodeVerification = false,
        IsExternal = true,
        InputRequired = false
    };

    private static CloudLoginProviderDefinition Password() => new("password")
    {
        HandlesEmailAddress = true,
        HandlesPhoneNumber = false,
        IsCodeVerification = false,
        IsExternal = false,
        InputRequired = true
    };

    private static async Task<List<RenderTreeFrame>> RenderSettledPageAsync(params CloudLoginProviderDefinition[] providers)
    {
        ServiceCollection services = new();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(StubCloudLogin.Create([.. providers]));
        services.AddSingleton<AuthenticationProcessService>();
        services.AddSingleton<NavigationManager, StubNavigationManager>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using SettledPageRenderer renderer = new(provider);

        return await renderer.RenderAsync<LoginComponent>();
    }

    private static bool OffersKeepSignedIn(List<RenderTreeFrame> page) =>
        page.Any(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "id" && Equals(frame.AttributeValue, "keepme"));

    private static bool Shows(List<RenderTreeFrame> page, string text) =>
        page.Any(frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == text);

    [Fact]
    public async Task It_is_offered_when_only_external_providers_are_configured()
    {
        List<RenderTreeFrame> page = await RenderSettledPageAsync(External("Google"), External("Microsoft"));

        Assert.True(OffersKeepSignedIn(page));
        Assert.True(Shows(page, "Google"));
        Assert.True(Shows(page, "Microsoft"));
    }

    [Fact]
    public async Task It_is_offered_alongside_an_address_and_external_providers()
    {
        List<RenderTreeFrame> page = await RenderSettledPageAsync(Password(), External("Google"));

        Assert.True(OffersKeepSignedIn(page));
        Assert.True(Shows(page, "Google"));
    }

    /// <summary>The component fixes its own render mode, which <c>HtmlRenderer</c> refuses, so this runs it in place and reads back what it settled on.</summary>
    private sealed class SettledPageRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        protected override RendererInfo RendererInfo { get; } = new("Test", true);

        protected override void HandleException(Exception exception) => ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId, IComponentActivator componentActivator, IComponentRenderMode renderMode) =>
            componentActivator.CreateInstance(componentType);

        public Task<List<RenderTreeFrame>> RenderAsync<TComponent>() where TComponent : IComponent => Dispatcher.InvokeAsync(async () =>
        {
            int componentId = AssignRootComponentId(InstantiateComponent(typeof(TComponent)));
            await RenderRootComponentAsync(componentId);

            ArrayRange<RenderTreeFrame> frames = GetCurrentRenderTreeFrames(componentId);
            return frames.Array.Take(frames.Count).ToList();
        });
    }

    private sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager() => Initialize("https://authority.test/", "https://authority.test/");
    }

    private class StubCloudLogin : DispatchProxy
    {
        private List<CloudLoginProviderDefinition> _providers = [];

        public static ICloudLogin Create(List<CloudLoginProviderDefinition> providers)
        {
            ICloudLogin proxy = Create<ICloudLogin, StubCloudLogin>();
            ((StubCloudLogin)(object)proxy)._providers = providers;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ICloudLogin.GetProviders) => Task.FromResult(_providers),
            nameof(ICloudLogin.IsAuthenticated) => Task.FromResult(false),
            _ => targetMethod?.ReturnType is { IsValueType: true } valueType ? Activator.CreateInstance(valueType) : null
        };
    }
}
