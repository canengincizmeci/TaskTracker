using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface ITaskShareService
    {
        Task<IResult> InviteUserToTask(InviteUserToTaskDto dto);
        Task<IResult> AcceptTaskInvitationAsync(int invitationId);
        Task<IResult> RejectTaskInvitationAsync(int invitationId);
        Task<IDataResult<List<TaskInvitationDto>>> GetMyPendingInvitationsAsync();
        Task<IDataResult<List<SharedTaskDto>>> GetMySharedTasksAsync();


    }
}
