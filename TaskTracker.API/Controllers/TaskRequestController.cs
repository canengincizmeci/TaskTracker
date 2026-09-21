using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Utilities.Results;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TaskRequestController : ControllerBase
    {

        private readonly ITaskRequestService _taskRequestService;
        private readonly ICurrentUserService _currentUserService;

        public TaskRequestController(ITaskRequestService taskRequestService, ICurrentUserService currentUserService)
        {
            _taskRequestService = taskRequestService;
            _currentUserService = currentUserService;
        }

        [HttpGet("get-task/{id}")]
        [Authorize(Roles = "Admin,User")]
        public async Task<IActionResult> GetTaskRequest(int id)
        {


            var currentUserId = _currentUserService.UserId;

            var result = await _taskRequestService.GetTaskById(id, _currentUserService.UserId);

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result.Data);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("list-alltasks")]
        public async Task<IActionResult> ListAllTaskRequests()
        {


            var result = await _taskRequestService.GetAllTasks();

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }

        [Authorize(Roles = "User")]
        [HttpPost("add-task")]
        public async Task<IActionResult> AddTaskRequest([FromBody] TaskRequestCreateDto taskRequestDto)
        {


            var currentUserId = _currentUserService.UserId;

            var result = await _taskRequestService.AddTaskRequestAsync(
                taskRequestDto,
                currentUserId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }

        [Authorize(Roles = "Admin,User")]
        [HttpDelete("delete-task/{id}")]
        public async Task<IActionResult> DeleteTaskRequest(int id)
        {

            var currentUserId = _currentUserService.UserId;

            var result = await _taskRequestService.DeleteTask(id, currentUserId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("update-task")]
        public async Task<IActionResult> UpdateTaskRequest(UpdateTaskRequestDto taskRequestDto)
        {

            var currentUserId = _currentUserService.UserId;

            var result = await _taskRequestService.UpdateTask(taskRequestDto, currentUserId);

            return ToActionResult(result);
        }

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/assign")]
        public async Task<IActionResult> Assign(int taskId, AssignTaskDto dto) =>
            ToActionResult(await _taskRequestService.AssignTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/unassign")]
        public async Task<IActionResult> Unassign(int taskId, TaskWorkflowCommandDto dto) =>
            ToActionResult(await _taskRequestService.UnassignTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/start")]
        public async Task<IActionResult> Start(int taskId, TaskWorkflowCommandDto dto) =>
            ToActionResult(await _taskRequestService.StartTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/complete")]
        public async Task<IActionResult> Complete(int taskId, TaskWorkflowCommandDto dto) =>
            ToActionResult(await _taskRequestService.CompleteTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/cancel")]
        public async Task<IActionResult> Cancel(int taskId, TaskWorkflowCommandDto dto) =>
            ToActionResult(await _taskRequestService.CancelTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/reopen")]
        public async Task<IActionResult> Reopen(int taskId, TaskWorkflowCommandDto dto) =>
            ToActionResult(await _taskRequestService.ReopenTaskAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpGet("list-user-tasks")]
        public async Task<IActionResult> ListUserTasks()
        {
            var currentUserId = _currentUserService.UserId;
            var result = await _taskRequestService.GetTasksByUserId(currentUserId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }

        [Authorize(Roles = "User")]
        [HttpGet("assigned-to-me")]
        public async Task<IActionResult> AssignedToMe()
        {
            var result = await _taskRequestService.GetAssignedTasksAsync(_currentUserService.UserId);
            return result.Success ? Ok(result.Data) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/submissions")]
        public async Task<IActionResult> Submit(int taskId, CreateTaskSubmissionDto dto) =>
            ToDataActionResult(await _taskRequestService.SubmitAsync(taskId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpGet("{taskId:int}/submissions")]
        public async Task<IActionResult> SubmissionHistory(int taskId) =>
            ToDataActionResult(await _taskRequestService.GetSubmissionHistoryAsync(taskId, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpPost("{taskId:int}/submissions/{submissionId:int}/review")]
        public async Task<IActionResult> Review(int taskId, int submissionId, ReviewTaskSubmissionDto dto) =>
            ToDataActionResult(await _taskRequestService.ReviewAsync(taskId, submissionId, dto, _currentUserService.UserId));

        [Authorize(Roles = "User")]
        [HttpGet("awaiting-review")]
        public async Task<IActionResult> AwaitingReview() =>
            ToDataActionResult(await _taskRequestService.GetAwaitingReviewAsync(_currentUserService.UserId));

        private IActionResult ToActionResult(TaskTracker.Core.Utilities.Results.IResult result) =>
            result.Success ? Ok(result.Message) : result switch
            {
                IConflictResult => Conflict(new { code = "conflict", message = result.Message }),
                _ when result.Message == Messages.AuthorizationDenied => StatusCode(403, result.Message),
                _ => BadRequest(result.Message)
            };

        private IActionResult ToDataActionResult<T>(IDataResult<T> result) => result.Success ? Ok(result.Data) :
            result switch
            {
                IConflictResult => Conflict(new { code = "conflict", message = result.Message }),
                _ when result.Message == Messages.AuthorizationDenied => StatusCode(403, result.Message),
                _ => BadRequest(result.Message)
            };

    }
}
