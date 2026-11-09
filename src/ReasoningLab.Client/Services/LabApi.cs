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
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string message = "We couldn't save that change. Please try again.";
            try { var error = await response.Content.ReadFromJsonAsync<JsonElement>(); if (error.TryGetProperty("error", out var e)) message = e.GetString()!; }
            catch (JsonException) { }
            throw new InvalidOperationException(message);
        }
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
