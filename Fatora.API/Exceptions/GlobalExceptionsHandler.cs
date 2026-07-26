using System.Text.Json;
using Fatora.BL.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Fatora.API.Exceptions;


public sealed class GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IOptions<HttpJsonOptions> jsonOptions) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            logger.LogError(exception, "Unhandled exception occured. TraceId: {TraceId}",
                httpContext.TraceIdentifier);

            var (statusCode, title) = MapException(exception);

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Type = GetProblemType(statusCode),
                Instance = httpContext.Request.Path,
                Detail = GetSafeErrorMessage(exception, httpContext)
            };

            problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
            problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

            httpContext.Response.StatusCode = statusCode;
            // Written directly rather than through IProblemDetailsService -
            // its default writer unconditionally resets Content-Type to
            // "application/problem+json" with no charset right after this
            // runs, which makes strict clients (e.g. Dart's http package)
            // fall back to Latin-1 and garble every Arabic error message.
            httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
            await httpContext.Response.WriteAsync(
                JsonSerializer.Serialize(problemDetails, jsonOptions.Value.SerializerOptions),
                cancellationToken);

            return true;
        }

        // Map the exceptions to HTTP responses
        private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
        {
            ArgumentNullException => (StatusCodes.Status400BadRequest, "Invalid argument provided"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid argument provided"),

            NotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
            ConflictException => (StatusCodes.Status409Conflict, "Resource Already Exists"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            BadRequestException => (StatusCodes.Status400BadRequest, "Invalid Request"),

            AppException appException => ((int)appException.StatusCode, "Application Error"),

            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };


        // Function to map the HTTP Status codes to the Problem Type URL
        private static string GetProblemType(int statusCode) => statusCode switch
        {
            400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            _ => "about:blank"
        };

        // Function to get safe error message in production and explicit error message in development
        private static string? GetSafeErrorMessage(Exception exception, HttpContext context) 
        {
            //only expose details in development
            var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                return exception.Message;
            }

            //In production, only expose message from custom safe exceptions
            return exception is AppException ? exception.Message : null;
        }
    }