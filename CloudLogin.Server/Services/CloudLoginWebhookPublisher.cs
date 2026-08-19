using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Server;

public sealed class CloudLoginWebhookPublisher(
    HttpClient httpClient,
    CloudLoginWebConfiguration configuration)
    : ICloudLoginEventPublisher
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(4)
    ];

    public async Task PublishAsync(
        CloudLoginEvent cloudEvent,
        CancellationToken cancellationToken = default)
    {
        List<CloudLoginWebhookRegistration> registrations =
        [
            .. configuration.Webhooks.Where(registration =>
                registration.Events.Count == 0
                || registration.Events.Contains(cloudEvent.EventType))
        ];

        foreach (CloudLoginWebhookRegistration registration in registrations)
            await DeliverAsync(registration, cloudEvent, cancellationToken);
    }

    private async Task DeliverAsync(
        CloudLoginWebhookRegistration registration,
        CloudLoginEvent cloudEvent,
        CancellationToken cancellationToken)
    {
        string payload = System.Text.Json.JsonSerializer.Serialize(
            cloudEvent,
            CloudLoginSerialization.Options);
        string signature = Sign(payload, registration.Secret);
        Exception? lastError = null;

        for (int attempt = 0; attempt < RetryDelays.Length; attempt++)
        {
            if (RetryDelays[attempt] > TimeSpan.Zero)
                await Task.Delay(RetryDelays[attempt], cancellationToken);

            try
            {
                using HttpRequestMessage request =
                    new(HttpMethod.Post, registration.Url);
                request.Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("X-CloudLogin-Event-Id", cloudEvent.EventId);
                request.Headers.Add(
                    "X-CloudLogin-Timestamp",
                    cloudEvent.Timestamp.ToUnixTimeSeconds().ToString());
                request.Headers.Add(
                    "X-CloudLogin-Signature",
                    $"sha256={signature}");
                request.Headers.UserAgent.Add(
                    new ProductInfoHeaderValue("CloudLogin", "1"));

                using HttpResponseMessage response =
                    await httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return;

                lastError = new HttpRequestException(
                    $"Webhook '{registration.Application}' returned {(int)response.StatusCode}.");
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                lastError = exception;
            }
        }

        throw new HttpRequestException(
            $"CloudLogin webhook delivery to '{registration.Application}' failed after {RetryDelays.Length} attempts.",
            lastError);
    }

    public static bool Verify(
        string payload,
        string signature,
        string secret)
    {
        string expected = $"sha256={Sign(payload, secret)}";
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature));
    }

    private static string Sign(string payload, string secret)
    {
        using HMACSHA256 hmac =
            new(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
