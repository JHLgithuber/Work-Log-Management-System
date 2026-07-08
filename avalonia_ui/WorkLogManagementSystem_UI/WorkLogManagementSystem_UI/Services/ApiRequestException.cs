using System;
using System.Net;
using System.Net.Http;

namespace WorkLogManagementSystem_UI.Services;

public sealed class ApiRequestException : HttpRequestException
{
    public ApiRequestException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string responseBody,
        string? contentType,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseBody = responseBody;
        ContentType = contentType;
        IsHtmlResponse = IsHtmlContent(contentType, responseBody);
    }

    public new HttpStatusCode StatusCode { get; }
    public string? ReasonPhrase { get; }
    public string ResponseBody { get; }
    public string? ContentType { get; }
    public bool IsHtmlResponse { get; }

    private static bool IsHtmlContent(string? contentType, string body)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string trimmedBody = body.TrimStart();
        return trimmedBody.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               trimmedBody.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }
}
