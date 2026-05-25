using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

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
            //int? currentUser =  _currentUserService.UserId;
            //if (!currentUser.HasValue)
            //{
            //    return BadRequest(Messages.UserNotFound);
            //}

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
            //int? currentUser = _currentUserService.UserId;
            //if (!currentUser.HasValue)
            //{
            //    return BadRequest(Messages.UserNotFound);
            //}

            var result = await _taskRequestService.GetAllTasks();

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }
         
        [Authorize(Roles = "User")]
        [HttpPost("add-task")]
        public async Task<IActionResult> AddTaskRequest(TaskRequestCreateDto taskRequestDto)
        {
            //int? currentUserId = _currentUserService.UserId;
            //if (!currentUserId.HasValue)
            //{
            //    return BadRequest(Messages.UserNotFound);
            //}

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
            //int? currentUserId = _currentUserService.UserId;
            //if (!currentUserId.HasValue)
            //{
            //    return BadRequest(Messages.UserNotFound);
            //}

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
            //int? currentUserId = _currentUserService.UserId;
            //if (!currentUserId.HasValue)
            //{
            //    return BadRequest(Messages.UserNotFound);
            //}

            var currentUserId = _currentUserService.UserId;

            var result = await _taskRequestService.UpdateTask(taskRequestDto, currentUserId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }
        
    }
}