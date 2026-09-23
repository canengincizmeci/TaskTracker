using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete;

public class WorkDashboardManager(
    ITaskRequestDal taskRequestDal,
    ICurrentUserService currentUserService) : IWorkDashboardService
{
    public async Task<IDataResult<WorkDashboardSummaryDto>> GetSummaryAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var summary = await taskRequestDal.GetWorkDashboardSummaryAsync(
            currentUserService.UserId,
            DateOnly.FromDateTime(nowUtc),
            nowUtc);

        return new SuccessDataResult<WorkDashboardSummaryDto>(summary);
    }
}
