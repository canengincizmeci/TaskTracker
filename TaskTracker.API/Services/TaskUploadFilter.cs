using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TaskTracker.Bussiness.Abstract;

namespace TaskTracker.API.Services;

public sealed class TaskUploadFilter(TaskUploadSlots slots, ICurrentUserService currentUser,
    ITaskCollaborationService collaboration) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!int.TryParse(context.RouteData.Values["taskId"] as string, NumberStyles.None,
                CultureInfo.InvariantCulture, out var taskId) || taskId <= 0)
        {
            context.Result = new BadRequestObjectResult("A positive task identifier is required.");
            return;
        }

        if (!slots.TryEnter())
        {
            http.Response.Headers.RetryAfter = "1";
            context.Result = new ObjectResult(new { code = "upload_busy", message = "Too many uploads. Try again shortly." })
                { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }

        try
        {
            if (!await collaboration.CanAccessAsync(taskId, currentUser.UserId, http.RequestAborted))
            {
                context.Result = new ForbidResult();
                return;
            }
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize is { } limit &&
                http.Request.ContentLength > limit)
            {
                context.Result = new ObjectResult("The upload exceeds the request size limit.")
                    { StatusCode = StatusCodes.Status413PayloadTooLarge };
                return;
            }

            await next();
        }
        finally
        {
            slots.Leave();
        }
    }
}

public sealed class TaskUploadSlots(IOptions<TaskFileOptions> options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(Math.Clamp(options.Value.MaxConcurrentUploads, 1, 32));

    internal bool TryEnter() => gate.Wait(0);
    internal void Leave() => gate.Release();

    public void Dispose() => gate.Dispose();
}
