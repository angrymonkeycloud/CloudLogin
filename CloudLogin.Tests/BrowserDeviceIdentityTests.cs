using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests;

public class BrowserDeviceIdentityTests
{
    private static ServiceProvider Services() => new ServiceCollection()
        .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()).BuildServiceProvider();

    [Fact]
    public void CookieSurvivesSignIn_AndCannotBeForged()
    {
        using ServiceProvider services = Services();
        DefaultHttpContext first = new() { RequestServices = services };
        first.Request.Scheme = "https";
        string identity = BrowserDeviceIdentity.GetOrCreate(first);
        string cookie = first.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        DefaultHttpContext next = new() { RequestServices = services };
        next.Request.Headers.Cookie = cookie.Split(';')[0];
        Assert.Equal(identity, BrowserDeviceIdentity.GetOrCreate(next));
        DefaultHttpContext forged = new() { RequestServices = services };
        forged.Request.Headers.Cookie = $"{BrowserDeviceIdentity.CookieName}={identity}";
        Assert.NotEqual(identity, BrowserDeviceIdentity.GetOrCreate(forged));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(90, 180, true)]
    [InlineData(-90, -180, true)]
    [InlineData(91, 0, false)]
    [InlineData(0, 181, false)]
    [InlineData(double.NaN, 0, false)]
    [InlineData(double.PositiveInfinity, 0, false)]
    public void CoordinatesMustBeValid(double latitude, double longitude, bool valid)
    {
        Assert.Equal(valid, new CloudLoginHistoryEntry { Latitude = latitude, Longitude = longitude }.HasCoordinates);
    }
}