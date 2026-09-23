using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;

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
}
