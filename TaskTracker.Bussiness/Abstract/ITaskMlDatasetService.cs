using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskMlDatasetService
{
    TaskMlDatasetSchemaDto GetSchema();
    Task<IDataResult<TaskMlDatasetExportDto>> ExportCsvAsync(int currentUserId, int? workspaceId = null,
        bool eligibleOnly = false, CancellationToken ct = default);
    Task<IDataResult<List<TaskHistoricalEligibilityDto>>> AnalyzeEligibilityAsync(int currentUserId,
        int? workspaceId = null, CancellationToken ct = default);
}
