using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers;

[Route("api/work-dashboard")]
[ApiController]
[Authorize(Roles = "User")]
public class WorkDashboardController(IWorkDashboardService workDashboardService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var result = await workDashboardService.GetSummaryAsync();
        return result.Success ? Ok(result.Data) : BadRequest(result.Message);
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks([FromQuery] WorkTaskQueryDto query)
    {
        var result = await workDashboardService.GetTasksAsync(query);
        return result.Success ? Ok(result.Data) : BadRequest(result.Message);
    }
}
