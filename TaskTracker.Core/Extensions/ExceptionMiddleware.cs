using Microsoft.AspNetCore.Http;
using FluentValidation;

namespace TaskTracker.Core.Extensions
{
    public class ExceptionMiddleware
    {
        private RequestDelegate _next;

        public ExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                if (!httpContext.Response.HasStarted)
                    httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
            catch (Exception e)
            {
                await HandleExceptionAsync(httpContext, e);
            }
        }

        private Task HandleExceptionAsync(HttpContext httpContext, Exception e)
        {
            httpContext.Response.ContentType = "application/json";
            if (e is ValidationException validation)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return httpContext.Response.WriteAsync(new ValidationErrorDetails
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = "validation_failed",
                    Message = ValidationFieldError.Summary,
                    Errors = validation.Errors.Select(ValidationFieldError.FromFailure).ToArray()
                }.ToString());
            }

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return httpContext.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = httpContext.Response.StatusCode,
                Message = "Internal Server Error"
            }.ToString());
        }

    }
}
