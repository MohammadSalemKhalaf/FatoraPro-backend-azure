using System.Net;

namespace Fatora.BL.Exceptions;

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(
        string message,
        string? errorCode = null,
        IReadOnlyDictionary<string, object>? extensions = null)
        : base(message, HttpStatusCode.Forbidden, errorCode, extensions)
    {
    }
}
