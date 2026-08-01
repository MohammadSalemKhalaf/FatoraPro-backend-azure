using System.Net;

namespace Fatora.BL.Exceptions;


public abstract class AppException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        // Machine-readable discriminator for a handful of cases where the frontend needs to react
        // to *why* a request failed, not just display the message - e.g. distinguishing a trial-
        // expired login rejection from a plain wrong-password one, both of which are otherwise the
        // same status code. Null for the common case where the message text is all the caller needs.
        public string? ErrorCode { get; }

        protected AppException(string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError, string? errorCode = null)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }