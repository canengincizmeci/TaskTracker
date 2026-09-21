using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TaskShareController : ControllerBase
    {
        private readonly ITaskShareService _taskShareService;
        private readonly ICurrentUserService _currentUserService;

        public TaskShareController(ITaskShareService taskShareService, ICurrentUserService currentUserService)
        {
            _taskShareService = taskShareService;
            _currentUserService = currentUserService;
        }

        [Authorize(Roles = "User")]
        [HttpPost("invite-user")]
        public async Task<IActionResult> InviteUserToTask(InviteUserToTaskDto dto)
        {
            var result = await _taskShareService.InviteUserToTask(dto);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("accept-invitation/{invitationId}")]
        public async Task<IActionResult> AcceptTaskInvitation(int invitationId)
        {
            var result = await _taskShareService.AcceptTaskInvitationAsync(invitationId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("reject-invitation/{invitationId}")]
        public async Task<IActionResult> RejectTaskInvitation(int invitationId)
        {
            var result = await _taskShareService.RejectTaskInvitationAsync(invitationId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }
        [Authorize(Roles = "User,Admin")]
        [HttpGet("get-user-invitations")]
        public async Task<IActionResult> GetMyPendingInvitationsAsync()
        {
            var result = await _taskShareService.GetMyPendingInvitationsAsync();

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);

        }  

        [Authorize(Roles = "User")]
        [HttpGet("invitation-details/{invitationId}")]
        public async Task<IActionResult> GetInvitation(int invitationId)
        {
            var result = await _taskShareService.GetInvitationAsync(invitationId);
            return result.Success ? Ok(result.Data) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User,Admin")]
        [HttpGet("task-participants/{taskId}")]
        public async Task<IActionResult> GetParticipants(int taskId)
        {
            var result = await _taskShareService.GetParticipantsAsync(taskId);
            return result.Success ? Ok(result.Data) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpGet("user-shared-tasks")]
        public async Task<IActionResult> GetMySharedTasksAsync()
        {
            var result = await _taskShareService.GetMySharedTasksAsync();

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }

        [Authorize(Roles = "User,Admin")]
        [HttpGet("shared-task-details/{taskShareId}")]
        public async Task<IActionResult> GetSharedTaskDetailsAsync(int taskShareId)
        {
            int? currentUserId = _currentUserService.UserId;
            if (!currentUserId.HasValue)
                return Unauthorized(Messages.AuthorizationDenied);

            var result = await _taskShareService.GetSharedTaskDetailsAsync(taskShareId, currentUserId.Value);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }

        [Authorize(Roles = "User")]
        [HttpGet("task/{taskId:int}/outgoing-invitations")]
        public async Task<IActionResult> GetOutgoingInvitations(int taskId)
        {
            var result = await _taskShareService.GetOutgoingInvitationsAsync(taskId);
            return result.Success ? Ok(result.Data) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("invitations/{invitationId:int}/cancel")]
        public async Task<IActionResult> CancelInvitation(int invitationId, TaskWorkflowCommandDto dto)
        {
            var result = await _taskShareService.CancelInvitationAsync(invitationId, dto.Version);
            return result.Success ? Ok(result.Message) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPut("task/{taskId:int}/participants/{userId:int}/permission")]
        public async Task<IActionResult> UpdatePermission(int taskId, int userId, UpdateParticipantPermissionDto dto)
        {
            var result = await _taskShareService.UpdateParticipantPermissionAsync(taskId, userId, dto);
            return result.Success ? Ok(result.Message) : BadRequest(result.Message);
        }

        [Authorize(Roles = "User")]
        [HttpPost("task/{taskId:int}/participants/{userId:int}/remove")]
        public async Task<IActionResult> RemoveParticipant(int taskId, int userId, TaskWorkflowCommandDto dto)
        {
            var result = await _taskShareService.RemoveParticipantAsync(taskId, userId, dto.Version);
            return result.Success ? Ok(result.Message) : BadRequest(result.Message);
        }

    }
}
