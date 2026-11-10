using System.Net.Http.Json;
using System.Text.Json;
using ReasoningLab.Core;

namespace ReasoningLab.Client.Services;

public class LabApi(HttpClient http)
{
    private bool ready;
    public async Task Initialize()
    {
        if (ready) return;
        await Send<JsonElement>(HttpMethod.Post, "api/profile", new { });
        ready = true;
    }
    public async Task<T> Get<T>(string route) => await Send<T>(HttpMethod.Get, route);
    public async Task<T> Post<T>(string route, object body, string? adminToken = null) => await Send<T>(HttpMethod.Post, route, body, adminToken);
    private async Task<T> Send<T>(HttpMethod method, string route, object? body = null, string? adminToken = null)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-ReasoningLab", "1");
        if (adminToken is not null) request.Headers.Add("X-Admin-Token", adminToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var rejected = (int)response.StatusCode is >= 400 and < 500 && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout;
                var message = rejected
                    ? "The server rejected this request. Refresh the session before trying again."
                    : "The service is temporarily unavailable. Please try again.";
                // Use validation messages for explicit rejections, never raw server error pages.
                if (rejected)
                {
                    try
                    {
                        var error = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
                        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("error", out var detail)
                            && detail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString()))
                            message = detail.GetString()!;
                    }
                    catch (Exception ex) when (ex is JsonException or HttpRequestException or OperationCanceledException) { }
                }
                throw new ApiFailure(rejected ? ApiFailureKind.Rejected : ApiFailureKind.Unavailable, message, response.StatusCode);
            }
            var result = await response.Content.ReadFromJsonAsync<T>(timeout.Token);
            return result ?? throw new ApiFailure(ApiFailureKind.InvalidResponse, "The service returned an unreadable response. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new ApiFailure(ApiFailureKind.Connection, "We couldn't reach the service. Check your connection and try again.");
        }
        catch (OperationCanceledException)
        {
            throw new ApiFailure(ApiFailureKind.Connection, "The request timed out. Check your connection and try again.");
        }
        catch (JsonException)
        {
            throw new ApiFailure(ApiFailureKind.InvalidResponse, "The service returned an unreadable response. Please try again.");
        }
    }
}
