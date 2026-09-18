using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Abstract;

namespace TaskTracker.DataAccess.Concrete.EfCore;

public class EfTaskWorkspaceDal : ITaskWorkspaceDal
{
    private readonly TaskTrackerDbContext _context;

    public EfTaskWorkspaceDal(TaskTrackerDbContext context)
    {
        _context = context;
    }

    public Task<bool> CanAccessAsync(int taskId, int userId) => _context.TaskRequests.AnyAsync(x =>
        x.Id == taskId && x.Activity && (x.OwnerId == userId || x.TaskShares.Any(s =>
            s.SharedWithUserId == userId && s.Permission >= TaskPermission.View && s.Permission <= TaskPermission.Manage)));

    public async Task<List<TaskActivity>> GetActivitiesAsync(int taskId)
    {
        var items = await _context.TaskActivities.AsNoTracking().Where(x => x.TaskRequestId == taskId)
            .Include(x => x.ActorUser).Include(x => x.TargetUser)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(100).ToListAsync();
        items.Reverse();
        return items;
    }

    public Task<TaskActivity?> GetActivityAsync(int activityId) => _context.TaskActivities.AsNoTracking()
        .Include(x => x.ActorUser).Include(x => x.TargetUser).SingleOrDefaultAsync(x => x.Id == activityId);

    public async Task<List<TaskMessage>> GetMessagesAsync(int taskId)
    {
        var items = await _context.TaskMessages.AsNoTracking().Where(x => x.TaskRequestId == taskId)
            .Include(x => x.SenderUser).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(100).ToListAsync();
        items.Reverse();
        return items;
    }
}
