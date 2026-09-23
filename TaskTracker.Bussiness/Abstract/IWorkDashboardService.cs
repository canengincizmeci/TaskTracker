using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface IWorkDashboardService
{
    Task<IDataResult<WorkDashboardSummaryDto>> GetSummaryAsync();
    Task<IDataResult<PagedWorkTasksDto>> GetTasksAsync(WorkTaskQueryDto query);
}
