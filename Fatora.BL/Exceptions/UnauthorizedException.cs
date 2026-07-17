using System.Net;

namespace Fatora.BL.Exceptions;

public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message)
        : base(message, HttpStatusCode.Unauthorized)
    {
    }
}
