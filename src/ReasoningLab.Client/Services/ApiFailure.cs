using System.Net;

namespace ReasoningLab.Client.Services;

public enum ApiFailureKind { Connection, Rejected, Unavailable, InvalidResponse }

// A failed transport, timeout or 5xx response cannot prove a write was not committed.
public sealed class ApiFailure(ApiFailureKind kind, string message, HttpStatusCode? status = null)
    : Exception(message)
{
    public ApiFailureKind Kind { get; } = kind;
    public HttpStatusCode? Status { get; } = status;
}
