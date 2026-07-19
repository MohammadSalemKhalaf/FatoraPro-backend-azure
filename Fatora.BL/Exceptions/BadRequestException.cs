using System.Net;

namespace Fatora.BL.Exceptions;

public sealed class BadRequestException : AppException
{
    public BadRequestException(string message)
        : base(message, HttpStatusCode.BadRequest)
    {
    }
}
