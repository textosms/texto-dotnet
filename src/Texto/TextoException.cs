using System;
using System.Text.Json;

namespace Texto
{
    /// <summary>Base type for every error raised by the Texto SDK.</summary>
    public class TextoException : Exception
    {
        /// <summary>Creates a new exception.</summary>
        public TextoException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>The request never completed (network failure or timeout).</summary>
    public class TextoConnectionException : TextoException
    {
        /// <summary>Creates a new connection exception.</summary>
        public TextoConnectionException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>The API returned a non-success status code.</summary>
    public class TextoApiException : TextoException
    {
        /// <summary>HTTP status code returned by the API.</summary>
        public int Status { get; }

        /// <summary>Raw JSON body of the error response, when one was returned.</summary>
        public JsonElement? Body { get; }

        /// <summary>Machine readable error code, when the API supplied one.</summary>
        public string? ErrorCode { get; }

        /// <summary>Request identifier, useful when contacting support.</summary>
        public string? RequestId { get; }

        /// <summary>Creates a new API exception.</summary>
        public TextoApiException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message)
        {
            Status = status;
            Body = body;
            ErrorCode = errorCode;
            RequestId = requestId;
        }

        internal int? IntFromBody(string property)
        {
            if (Body is JsonElement element &&
                element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(property, out var value) &&
                value.TryGetInt32(out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }

    /// <summary>The API key is missing, malformed or revoked (401).</summary>
    public class TextoAuthenticationException : TextoApiException
    {
        /// <summary>Creates a new authentication exception.</summary>
        public TextoAuthenticationException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }

    /// <summary>The key is valid but not allowed to perform this action (403).</summary>
    public class TextoPermissionException : TextoApiException
    {
        /// <summary>Creates a new permission exception.</summary>
        public TextoPermissionException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }

    /// <summary>The request was rejected as invalid (400 or 422).</summary>
    public class TextoInvalidRequestException : TextoApiException
    {
        /// <summary>Creates a new invalid request exception.</summary>
        public TextoInvalidRequestException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }

    /// <summary>The account has too few credits, or the saved card failed (402).</summary>
    public class TextoInsufficientCreditsException : TextoApiException
    {
        /// <summary>Credits the request needed, when reported.</summary>
        public int? CreditsRequired => IntFromBody("credits_required");

        /// <summary>Credits currently available, when reported.</summary>
        public int? CreditsAvailable => IntFromBody("credits_available");

        /// <summary>Creates a new insufficient credits exception.</summary>
        public TextoInsufficientCreditsException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }

    /// <summary>The requested resource does not exist (404).</summary>
    public class TextoNotFoundException : TextoApiException
    {
        /// <summary>Creates a new not found exception.</summary>
        public TextoNotFoundException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }

    /// <summary>Too many requests were sent (429).</summary>
    public class TextoRateLimitException : TextoApiException
    {
        /// <summary>Seconds to wait before retrying, when the API supplied a Retry-After header.</summary>
        public int? RetryAfter { get; }

        /// <summary>Creates a new rate limit exception.</summary>
        public TextoRateLimitException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null, int? retryAfter = null)
            : base(message, status, body, errorCode, requestId)
        {
            RetryAfter = retryAfter;
        }
    }

    /// <summary>The API failed to handle the request (5xx).</summary>
    public class TextoServerException : TextoApiException
    {
        /// <summary>Creates a new server exception.</summary>
        public TextoServerException(string message, int status, JsonElement? body = null, string? errorCode = null, string? requestId = null)
            : base(message, status, body, errorCode, requestId) { }
    }
}
