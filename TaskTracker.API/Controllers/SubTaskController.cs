using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using ServiceResult = TaskTracker.Core.Utilities.Results.IResult;

namespace TaskTracker.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,User")]
[Route("api/TaskRequest/{taskId:int}/subtasks")]
public class SubTaskController(ISubTaskService subTasks, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(int taskId, CreateSubTaskDto dto)
    {
        var result = await subTasks.CreateAsync(taskId, dto, currentUser.UserId);
        return result.Success
            ? CreatedAtAction(nameof(Get), new { taskId, subTaskId = result.Data.Id }, result.Data)
            : Failure(result);
    }

    [HttpGet]
    public async Task<IActionResult> List(int taskId) =>
        Respond(await subTasks.ListAsync(taskId, currentUser.UserId));

    [HttpGet("/api/TaskRequest/assigned-subtasks")]
    public async Task<IActionResult> Assigned() =>
        Respond(await subTasks.ListAssignedAsync(currentUser.UserId));

    [HttpGet("{subTaskId:int}")]
    public async Task<IActionResult> Get(int taskId, int subTaskId) =>
        Respond(await subTasks.GetAsync(taskId, subTaskId, currentUser.UserId));

    [HttpPut("{subTaskId:int}")]
    public async Task<IActionResult> Update(int taskId, int subTaskId, UpdateSubTaskDto dto) =>
        Respond(await subTasks.UpdateAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpDelete("{subTaskId:int}")]
    public async Task<IActionResult> Delete(int taskId, int subTaskId, [FromQuery] long version = -1)
    {
        var result = await subTasks.DeleteAsync(taskId, subTaskId, new() { Version = version }, currentUser.UserId);
        return result.Success ? NoContent() : Failure(result);
    }

    [HttpPost("{subTaskId:int}/assign")]
    public async Task<IActionResult> Assign(int taskId, int subTaskId, AssignSubTaskDto dto) =>
        Respond(await subTasks.AssignAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/unassign")]
    public async Task<IActionResult> Unassign(int taskId, int subTaskId, SubTaskCommandDto dto) =>
        Respond(await subTasks.UnassignAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/start")]
    public async Task<IActionResult> Start(int taskId, int subTaskId, SubTaskCommandDto dto) =>
        Respond(await subTasks.StartAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/complete")]
    public async Task<IActionResult> Complete(int taskId, int subTaskId, SubTaskCommandDto dto) =>
        Respond(await subTasks.CompleteAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/cancel")]
    public async Task<IActionResult> Cancel(int taskId, int subTaskId, SubTaskCommandDto dto) =>
        Respond(await subTasks.CancelAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/reopen")]
    public async Task<IActionResult> Reopen(int taskId, int subTaskId, SubTaskCommandDto dto) =>
        Respond(await subTasks.ReopenAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpPost("{subTaskId:int}/submissions")]
    public async Task<IActionResult> Submit(int taskId, int subTaskId, CreateSubTaskSubmissionDto dto) =>
        Respond(await subTasks.SubmitAsync(taskId, subTaskId, dto, currentUser.UserId));

    [HttpGet("{subTaskId:int}/submissions")]
    public async Task<IActionResult> SubmissionHistory(int taskId, int subTaskId) =>
        Respond(await subTasks.GetSubmissionHistoryAsync(taskId, subTaskId, currentUser.UserId));

    [HttpPost("{subTaskId:int}/submissions/{submissionId:int}/review")]
    public async Task<IActionResult> Review(int taskId, int subTaskId, int submissionId, ReviewSubTaskSubmissionDto dto) =>
        Respond(await subTasks.ReviewAsync(taskId, subTaskId, submissionId, dto, currentUser.UserId));

    [HttpGet("{subTaskId:int}/activity")]
    public async Task<IActionResult> Activity(int taskId, int subTaskId) =>
        Respond(await subTasks.GetActivityHistoryAsync(taskId, subTaskId, currentUser.UserId));

    private IActionResult Respond<T>(IDataResult<T> result) => result.Success ? Ok(result.Data) : Failure(result);

    private IActionResult Failure(ServiceResult result) => result switch
    {
        IConflictResult => Conflict(new { code = "conflict", message = result.Message }),
        _ when result.Message == Messages.DataNotFound => NotFound(result.Message),
        _ when result.Message == Messages.AuthorizationDenied => StatusCode(403, result.Message),
        _ => BadRequest(result.Message)
    };
}
