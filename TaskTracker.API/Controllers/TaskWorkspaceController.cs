using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/TaskWorkspace/{taskId:int}")]
    public class TaskWorkspaceController : ControllerBase
    {
        private readonly ITaskWorkspaceService _workspace;
        private readonly ICurrentUserService _currentUser;

        public TaskWorkspaceController(ITaskWorkspaceService workspace, ICurrentUserService currentUser)
        {
            _workspace = workspace;
            _currentUser = currentUser;
        }

        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity(int taskId)
        {
            var result = await _workspace.GetActivitiesAsync(taskId, _currentUser.UserId);
            return result.Success ? Ok(result.Data) : StatusCode(403, result.Message);
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetMessages(int taskId)
        {
            var result = await _workspace.GetMessagesAsync(taskId, _currentUser.UserId);
            return result.Success ? Ok(result.Data) : StatusCode(403, result.Message);
        }

        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage(int taskId, CreateTaskMessageDto dto)
        {
            var result = await _workspace.SendMessageAsync(taskId, _currentUser.UserId, dto.Content);
            if (result.Success) return Ok(result.Data);
            return result.Message == Messages.AuthorizationDenied
                ? StatusCode(403, result.Message) : BadRequest(result.Message);
        }
    }
}
