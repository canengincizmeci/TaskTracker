using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TaskShareController : ControllerBase
    {
        private readonly ITaskShareService _taskShareService;

        public TaskShareController(ITaskShareService taskShareService)
        {
            _taskShareService = taskShareService;
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
    }
}
