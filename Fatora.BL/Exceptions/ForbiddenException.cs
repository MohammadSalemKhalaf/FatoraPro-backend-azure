using System.Net;

namespace Fatora.BL.Exceptions;

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message)
        : base(message, HttpStatusCode.Forbidden)
    {
    }
}
