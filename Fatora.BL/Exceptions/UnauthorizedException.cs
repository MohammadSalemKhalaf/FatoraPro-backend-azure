using System.Net;

namespace Fatora.BL.Exceptions;

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message, string? errorCode = null)
        : base(message, HttpStatusCode.Unauthorized, errorCode)
    {
    }
}
