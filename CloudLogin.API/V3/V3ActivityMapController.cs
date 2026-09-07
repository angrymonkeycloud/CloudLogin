using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.V3;

[Authorize]
[Route("api/v3/activity")]
public sealed class V3ActivityMapController(CloudLoginWebConfiguration configuration, ICloudLogin server, IHttpClientFactory clients) : ControllerBase
{


    [HttpGet("{id:guid}/map")]
    public async Task<IActionResult> GetMap(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(configuration.MapsSubscriptionKey))
            return NotFound();

        CloudLoginHistoryEntry? entry = (await server.GetMyLoginHistory()).FirstOrDefault(entry => entry.Id == id);
        if (entry is null || !entry.HasCoordinates)
            return NotFound();

        string center = FormattableString.Invariant($"{entry.Longitude},{entry.Latitude}");
        string pins = Uri.EscapeDataString(FormattableString.Invariant($"default||{entry.Longitude} {entry.Latitude}"));
        using HttpRequestMessage request = new(HttpMethod.Get,
            $"https://atlas.microsoft.com/map/static?api-version=2024-04-01&tilesetId=microsoft.base.road&zoom=12&width=320&height=180&center={center}&pins={pins}");
        request.Headers.Add("subscription-key", configuration.MapsSubscriptionKey);
        try
        {
            using HttpClient maps = clients.CreateClient("CloudLogin.ActivityMaps");
            maps.Timeout = TimeSpan.FromSeconds(10);
            using HttpResponseMessage response = await maps.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return StatusCode(503);
            string? contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not ("image/png" or "image/jpeg"))
                return StatusCode(503);
            return File(await response.Content.ReadAsByteArrayAsync(cancellationToken), contentType);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503);
        }
    }
}