using System;
using System.Net;

namespace Jellyfin.Plugin.Prdb.Api;

/// <summary>
/// The prdb API could not answer a request, after whatever retries the failure allowed.
/// </summary>
public class PrdbApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PrdbApiException"/> class.</summary>
    public PrdbApiException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrdbApiException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public PrdbApiException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrdbApiException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public PrdbApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrdbApiException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="statusCode">The HTTP status the API answered with, if it answered.</param>
    public PrdbApiException(string message, HttpStatusCode? statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>Gets the HTTP status the API answered with, or <c>null</c> when it did not answer.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Gets a value indicating whether the API rejected the key (401 or 403), which no retry fixes.</summary>
    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
