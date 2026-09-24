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

    public async Task<IDataResult<PagedWorkTasksDto>> GetTasksAsync(WorkTaskQueryDto query)
    {
        if (!Enum.IsDefined(query.Scope) || !Enum.IsDefined(query.Due) || !Enum.IsDefined(query.Sort) ||
            query.Status.HasValue && !Enum.IsDefined(query.Status.Value) ||
            query.Priority.HasValue && !Enum.IsDefined(query.Priority.Value))
            return new ErrorDataResult<PagedWorkTasksDto>("One or more work query filters are invalid.");
        if (query.Page < 1) return new ErrorDataResult<PagedWorkTasksDto>("Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            return new ErrorDataResult<PagedWorkTasksDto>("Page size must be between 1 and 100.");

        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var page = await taskRequestDal.GetWorkTasksAsync(currentUserService.UserId, query, todayUtc);
        return new SuccessDataResult<PagedWorkTasksDto>(page);
    }
}
