using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Tests;

public class WorkspaceModelTests
{
    [Fact]
    public async Task Workspace_is_created_with_one_owner_and_a_trimmed_name()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.Workspaces.Add(CreateWorkspace("  Product  ", Owner(1)));

        await context.SaveChangesAsync();

        var stored = await context.Workspaces.Include(x => x.Members).SingleAsync();
        Assert.Equal("Product", stored.Name);
        var owner = Assert.Single(stored.Members);
        Assert.Equal(WorkspaceRole.Owner, owner.Role);
        Assert.True(owner.IsActive);
        Assert.Null(owner.RemovedAt);
    }

    [Fact]
    public async Task Blank_workspace_name_is_rejected()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.Workspaces.Add(CreateWorkspace("   ", Owner(1)));

        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_membership_for_workspace_and_user_is_rejected()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.Workspaces.Add(CreateWorkspace("Product", Owner(1), Member(2)));
        await context.SaveChangesAsync();

        context.WorkspaceMembers.Add(Member(2, context.Workspaces.Single().Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_active_owners_for_one_workspace_are_rejected()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.Workspaces.Add(CreateWorkspace("Product", Owner(1), Owner(2)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Roles_persist_and_soft_removed_membership_remains_stored()
    {
        using var database = new TestDatabase();
        await using (var context = database.CreateContext())
        {
            context.Workspaces.Add(CreateWorkspace("Product", Owner(1), new WorkspaceMember
            {
                UserId = 2,
                Role = WorkspaceRole.Admin,
                IsActive = false,
                RemovedAt = DateTime.UtcNow
            }, Member(3)));
            await context.SaveChangesAsync();
        }

        await using var fresh = database.CreateContext();
        var members = await fresh.WorkspaceMembers.OrderBy(x => x.UserId).ToListAsync();
        Assert.Equal([WorkspaceRole.Owner, WorkspaceRole.Admin, WorkspaceRole.Member], members.Select(x => x.Role));
        Assert.False(members[1].IsActive);
        Assert.NotNull(members[1].RemovedAt);
        Assert.Equal(3, members.Count);
    }

    [Theory]
    [InlineData(WorkspaceInvitationStatus.Pending)]
    [InlineData(WorkspaceInvitationStatus.Accepted)]
    [InlineData(WorkspaceInvitationStatus.Rejected)]
    [InlineData(WorkspaceInvitationStatus.Cancelled)]
    [InlineData(WorkspaceInvitationStatus.Expired)]
    public async Task Invitation_statuses_persist(WorkspaceInvitationStatus status)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = CreateWorkspace("Product", Owner(1));
        workspace.Invitations.Add(new WorkspaceInvitation
        {
            InvitedUserId = 2,
            InvitedByUserId = 1,
            Status = status
        });
        context.Workspaces.Add(workspace);

        await context.SaveChangesAsync();

        Assert.Equal(status, (await context.WorkspaceInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Only_one_pending_invitation_per_workspace_and_invitee_is_allowed()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = CreateWorkspace("Product", Owner(1));
        workspace.Invitations.Add(Invitation(2, WorkspaceInvitationStatus.Pending));
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();

        context.WorkspaceInvitations.Add(Invitation(2, WorkspaceInvitationStatus.Pending, workspace.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Expired_invitation_does_not_block_a_new_pending_invitation()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = CreateWorkspace("Product", Owner(1));
        workspace.Invitations.Add(Invitation(2, WorkspaceInvitationStatus.Expired));
        workspace.Invitations.Add(Invitation(2, WorkspaceInvitationStatus.Pending));
        context.Workspaces.Add(workspace);

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.WorkspaceInvitations.CountAsync());
    }

    [Fact]
    public async Task Workspace_activity_is_append_only()
    {
        using var database = new TestDatabase();
        int activityId;
        await using (var context = database.CreateContext())
        {
            var workspace = CreateWorkspace("Product", Owner(1));
            workspace.Activities.Add(new WorkspaceActivity
            {
                ActorUserId = 1,
                ActivityType = WorkspaceActivityType.WorkspaceCreated
            });
            context.Workspaces.Add(workspace);
            await context.SaveChangesAsync();
            activityId = workspace.Activities.Single().Id;
        }

        await using (var modifyContext = database.CreateContext())
        {
            var activity = await modifyContext.WorkspaceActivities.SingleAsync(x => x.Id == activityId);
            modifyContext.Entry(activity).Property(x => x.ActivityType).CurrentValue = WorkspaceActivityType.MemberRemoved;
            await Assert.ThrowsAsync<InvalidOperationException>(() => modifyContext.SaveChangesAsync());
        }

        await using var deleteContext = database.CreateContext();
        var toDelete = await deleteContext.WorkspaceActivities.SingleAsync(x => x.Id == activityId);
        deleteContext.WorkspaceActivities.Remove(toDelete);
        await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Versioned_workspace_entities_use_optimistic_concurrency()
    {
        using var database = new TestDatabase();
        int workspaceId;
        await using (var setup = database.CreateContext())
        {
            var workspace = CreateWorkspace("Product", Owner(1), Member(2));
            workspace.Invitations.Add(Invitation(3, WorkspaceInvitationStatus.Pending));
            setup.Workspaces.Add(workspace);
            await setup.SaveChangesAsync();
            workspaceId = workspace.Id;
        }

        await using (var context = database.CreateContext())
        {
            Assert.True(context.Model.FindEntityType(typeof(Workspace))!.FindProperty(nameof(Workspace.Version))!.IsConcurrencyToken);
            Assert.True(context.Model.FindEntityType(typeof(WorkspaceMember))!.FindProperty(nameof(WorkspaceMember.Version))!.IsConcurrencyToken);
            Assert.True(context.Model.FindEntityType(typeof(WorkspaceInvitation))!.FindProperty(nameof(WorkspaceInvitation.Version))!.IsConcurrencyToken);

            var workspace = await context.Workspaces.SingleAsync(x => x.Id == workspaceId);
            var member = await context.WorkspaceMembers.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == 2);
            var invitation = await context.WorkspaceInvitations.SingleAsync(x => x.WorkspaceId == workspaceId);
            workspace.Name = "Renamed";
            member.Role = WorkspaceRole.Admin;
            invitation.Status = WorkspaceInvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            Assert.Equal(1, workspace.Version);
            Assert.Equal(1, member.Version);
            Assert.Equal(1, invitation.Version);
        }

        await using var first = database.CreateContext();
        await using var stale = database.CreateContext();
        var firstWorkspace = await first.Workspaces.SingleAsync(x => x.Id == workspaceId);
        var staleWorkspace = await stale.Workspaces.SingleAsync(x => x.Id == workspaceId);
        firstWorkspace.Name = "First update";
        staleWorkspace.Name = "Stale update";
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public void PostgreSql_model_generates_the_partial_unique_indexes()
    {
        using var context = new TaskTracker.Core.DataAccess.TaskTrackerDbContext(
            new DbContextOptionsBuilder<TaskTracker.Core.DataAccess.TaskTrackerDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=workspace_model;Username=model_check")
                .Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE UNIQUE INDEX \"IX_WorkspaceMembers_WorkspaceId\"", script);
        Assert.Contains("WHERE \"IsActive\" = TRUE AND \"Role\" = 'Owner'", script);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_WorkspaceInvitations_WorkspaceId_InvitedUserId\"", script);
        Assert.Contains("WHERE \"Status\" = 'Pending'", script);
    }

    private static Workspace CreateWorkspace(string name, params WorkspaceMember[] members) => new()
    {
        Name = name,
        Members = members.ToList()
    };

    private static WorkspaceMember Owner(int userId) => new() { UserId = userId, Role = WorkspaceRole.Owner };

    private static WorkspaceMember Member(int userId, int workspaceId = 0) => new()
    {
        WorkspaceId = workspaceId,
        UserId = userId,
        Role = WorkspaceRole.Member
    };

    private static WorkspaceInvitation Invitation(int invitedUserId, WorkspaceInvitationStatus status,
        int workspaceId = 0) => new()
    {
        WorkspaceId = workspaceId,
        InvitedUserId = invitedUserId,
        InvitedByUserId = 1,
        Status = status
    };
}
