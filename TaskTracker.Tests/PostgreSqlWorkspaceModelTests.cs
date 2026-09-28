using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Tests;

public class PostgreSqlWorkspaceModelTests
{
    [PostgreSqlFact]
    public async Task PostgreSql_enforces_active_owner_and_pending_invitation_partial_unique_indexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateCurrentModelAsync();
        int workspaceId;
        await using (var setup = database.CreateContext())
        {
            var workspace = new Workspace
            {
                Name = "PostgreSQL workspace",
                Members = [new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner }],
                Invitations = [new WorkspaceInvitation
                {
                    InvitedUserId = 2,
                    InvitedByUserId = 1,
                    Status = WorkspaceInvitationStatus.Pending
                }]
            };
            setup.Workspaces.Add(workspace);
            await setup.SaveChangesAsync();
            workspaceId = workspace.Id;
        }

        await using (var ownerContext = database.CreateContext())
        {
            ownerContext.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                UserId = 2,
                Role = WorkspaceRole.Owner
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ownerContext.SaveChangesAsync());
        }

        await using var invitationContext = database.CreateContext();
        invitationContext.WorkspaceInvitations.Add(new WorkspaceInvitation
        {
            WorkspaceId = workspaceId,
            InvitedUserId = 2,
            InvitedByUserId = 1,
            Status = WorkspaceInvitationStatus.Pending
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => invitationContext.SaveChangesAsync());
    }
}
