using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.Concrete;

namespace TaskTracker.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,User")]
[Route("api/ml-dataset")]
public class MlDatasetController(
    ITaskMlDatasetService datasetService,
    ICurrentUserService currentUserService) : ControllerBase
{
    public const string SchemaVersionHeader = "X-Dataset-Schema-Version";

    [HttpGet("schema")]
    public IActionResult GetSchema()
    {
        var schema = datasetService.GetSchema();
        Response?.Headers.Append(SchemaVersionHeader, schema.SchemaVersion);
        return Ok(schema);
    }

    [HttpGet("export")]
    [ServiceFilter(typeof(MlDatasetResourceFilter))]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] int? workspaceId = null,
        [FromQuery] bool eligibleOnly = false,
        CancellationToken ct = default)
    {
        var result = await datasetService.ExportCsvAsync(
            currentUserService.UserId,
            workspaceId,
            eligibleOnly,
            ct);

        if (!result.Success)
        {
            return Failure(result.Message);
        }

        Response?.Headers.Append(SchemaVersionHeader, result.Data.SchemaVersion);
        var bytes = Encoding.UTF8.GetBytes(result.Data.CsvContent);
        return File(bytes, "text/csv; charset=utf-8", result.Data.FileName);
    }

    [HttpGet("eligibility")]
    [ServiceFilter(typeof(MlDatasetResourceFilter))]
    public async Task<IActionResult> AnalyzeEligibility(
        [FromQuery] int? workspaceId = null,
        CancellationToken ct = default)
    {
        var result = await datasetService.AnalyzeEligibilityAsync(
            currentUserService.UserId,
            workspaceId,
            ct);

        if (!result.Success)
        {
            return Failure(result.Message);
        }

        return Ok(result.Data);
    }

    private IActionResult Failure(string message) => message switch
    {
        _ when message == Messages.AuthorizationDenied => StatusCode(StatusCodes.Status403Forbidden, message),
        TaskMlDatasetManager.DatasetLimitExceeded => UnprocessableEntity(message),
        _ => BadRequest(message)
    };
}
