using Microsoft.AspNetCore.Mvc;
using TaskTracker.API.Context;
using TaskTracker.API.DTOs;
using TaskTracker.API.Entitites;
using TaskTracker.API.FluentValidation;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TaskRequestController : ControllerBase
    {
        private readonly MyDbContext _context;
        private readonly TaskRequestValidator _validator;

        public TaskRequestController(MyDbContext context, TaskRequestValidator validator)
        {
            _context = context;
            _validator = validator;
        }

        [HttpGet("{id}")]
        public IActionResult GetTaskRequest(int id)
        {
            var taskRequest = _context.TaskRequests
                .FirstOrDefault(x => x.Id == id && x.Activity);

            if (taskRequest is null)
                return NotFound();

            return Ok(taskRequest);
        }

        [HttpGet]
        public IActionResult ListAllTaskRequests()
        {
            var taskRequests = _context.TaskRequests
                .Where(x => x.Activity)
                .ToList();

            return Ok(taskRequests);
        }

        [HttpPost]
        public IActionResult AddTaskRequest(
    [FromHeader(Name = "X-Admin-Token")] string adminToken,
    TaskRequestDto taskRequestDto)
        {
            if (!IsAdminAuthorized(adminToken))
                return Unauthorized("Admin authorization required.");

            var validationResult = _validator.Validate(taskRequestDto);

            if (!validationResult.IsValid)
                return BadRequest(validationResult.Errors);

            var taskRequest = new TaskRequest
            {
                Category = taskRequestDto.Category,
                CreatedAt = DateTime.UtcNow,
                Description = taskRequestDto.Description,
                Priority = taskRequestDto.Priority,
                Status = taskRequestDto.Status,
                Title = taskRequestDto.Title,
                Activity = true
            };

            _context.TaskRequests.Add(taskRequest);
            _context.SaveChanges();

            return Ok(taskRequest);
        }

        [HttpPost("{id}")]
        public IActionResult DeleteTaskRequest(
            int id,
            [FromHeader(Name = "X-Admin-Token")] string adminToken)
        {
            if (!IsAdminAuthorized(adminToken))
                return Unauthorized("Admin authorization required.");

            var taskRequest = _context.TaskRequests.Find(id);

            if (taskRequest is null)
                return NotFound();

            taskRequest.Activity = false;
            _context.SaveChanges();

            return Ok("Task request deleted.");
        }

        private bool IsAdminAuthorized(string adminToken)
        {
            if (string.IsNullOrWhiteSpace(adminToken))
                return false;

            return _context.AdminSessions.Any(x =>
                x.Token == adminToken &&
                !x.IsRevoked &&
                x.ExpireAt > DateTime.UtcNow);
        }
    }
}