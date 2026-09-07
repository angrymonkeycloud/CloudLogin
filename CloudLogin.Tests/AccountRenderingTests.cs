using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Tests;

public class AccountRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecurityShowsThreeEntries_DevicesSectionShowsAll(bool devicesOnly)
    {
        ICloudLogin login = DispatchProxy.Create<ICloudLogin, AccountProxy>();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(login);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IJSRuntime>(new NoJavaScript());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<AccountComponent_Security>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["CurrentUser"] = new CloudUserModel(),
                ["DevicesOnly"] = devicesOnly
            }));
            return root.ToHtmlString();
        });
        Assert.Equal(devicesOnly ? 5 : 3, Regex.Matches(html, @"entry-title[^>]*>\s*Device number").Count);
        Assert.Equal(devicesOnly ? 0 : 3, Regex.Matches(html, "Activity number").Count);
        if (!devicesOnly)
            Assert.Contains("View more (5)", html);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/api/v3/activity/preview/map", true)]
    public async Task LocationAlwaysLinksToGoogle_OnlyConfiguredMapsShowPreview(string? preview, bool hasImage)
    {
        ServiceCollection services = new();
        services.AddLogging();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ActivityLocation>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Entry"] = new CloudLoginHistoryEntryModel { Latitude = 33.89, Longitude = 35.5, MapImageUrl = preview }
            }));
            return root.ToHtmlString();
        });
        Assert.Contains("https://www.google.com/maps/search/?api=1", html);
        Assert.Contains("33.89%2C35.5", html);
        Assert.Contains("View on Map", html);
        Assert.Equal(hasImage, html.Contains("<img"));
    }

    public class AccountProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "GetSecurityOverview" => Task.FromResult(new CloudLoginSecurityOverview()),
            "GetMyDevices" => Task.FromResult(Enumerable.Range(1, 5).Select(i => new CloudLoginSignedInDevice
            {
                DeviceId = i.ToString(), Name = $"Device number {i}", OperatingSystem = "Windows", IsActive = true
            }).ToList()),
            "GetMyLoginHistory" => Task.FromResult(Enumerable.Range(1, 5).Select(i => new CloudLoginHistoryEntry
            {
                Provider = $"Activity number {i}", SignedInOn = DateTimeOffset.UtcNow.AddDays(-i)
            }).ToList()),
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/Account");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
}