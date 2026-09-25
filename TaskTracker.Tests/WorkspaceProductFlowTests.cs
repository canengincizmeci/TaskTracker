using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.API.Controllers;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class WorkspaceProductFlowTests
{
    [Fact]
    public async Task Create_is_atomic_and_creates_owner_and_audit_activity()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var result = await Manager(context, 1).CreateAsync(new CreateWorkspaceDto { Name = "  Product  " });

        Assert.True(result.Success);
        Assert.Equal("Product", result.Data!.Name);
        Assert.Equal(WorkspaceRole.Owner, result.Data.CurrentUserRole);
        Assert.Equal(1, result.Data.MemberCount);
        var member = await context.WorkspaceMembers.SingleAsync();
        Assert.Equal(1, member.UserId);
        Assert.Equal(WorkspaceRole.Owner, member.Role);
        Assert.True(member.IsActive);
        Assert.Equal(WorkspaceActivityType.WorkspaceCreated,
            (await context.WorkspaceActivities.SingleAsync()).ActivityType);

        var invalid = await Manager(context, 2).CreateAsync(new CreateWorkspaceDto { Name = " " });
        Assert.False(invalid.Success);
        Assert.Equal(1, await context.Workspaces.CountAsync());
        Assert.Equal(1, await context.WorkspaceMembers.CountAsync());
        Assert.Equal(1, await context.WorkspaceActivities.CountAsync());
    }

    [Fact]
    public async Task Lists_and_details_are_limited_to_active_members()
    {
        using var database = new TestDatabase();
        int workspaceId;
        await using (var setup = database.CreateContext())
        {
            workspaceId = await SeedWorkspace(setup, Member(1, WorkspaceRole.Owner), Member(2));
        }

        await using (var memberContext = database.CreateContext())
        {
            var manager = Manager(memberContext, 2);
            Assert.Single((await manager.GetMineAsync()).Data!);
            var details = await manager.GetDetailsAsync(workspaceId);
            Assert.True(details.Success);
            Assert.Equal(WorkspaceRole.Member, details.Data!.CurrentUserRole);
            Assert.Equal(2, details.Data.MemberCount);
        }

        await using (var removeContext = database.CreateContext())
        {
            var membership = await removeContext.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
            membership.IsActive = false;
            membership.RemovedAt = DateTime.UtcNow;
            await removeContext.SaveChangesAsync();
        }

        await using var removedContext = database.CreateContext();
        var removedManager = Manager(removedContext, 2);
        Assert.Empty((await removedManager.GetMineAsync()).Data!);
        Assert.False((await removedManager.GetDetailsAsync(workspaceId)).Success);
        Assert.False((await Manager(removedContext, 3).GetDetailsAsync(workspaceId)).Success);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Member, false)]
    public async Task Only_owner_and_admin_can_invite(WorkspaceRole actorRole, bool allowed)
    {
        using var database = new TestDatabase();
        int workspaceId;
        await using var context = database.CreateContext();
        workspaceId = await SeedWorkspace(context, Member(1, actorRole));

        var result = await Manager(context, 1).InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user2" });

        Assert.Equal(allowed, result.Success);
        Assert.Equal(allowed ? 1 : 0, await context.WorkspaceInvitations.CountAsync());
        Assert.Equal(allowed, (await Manager(context, 1).GetInvitationsAsync(workspaceId)).Success);
    }

    [Fact]
    public async Task Invite_rejects_self_active_members_and_duplicate_live_pending_invitations()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner), Member(2));
        var manager = Manager(context, 1);

        Assert.False((await manager.InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user1" })).Success);
        Assert.IsAssignableFrom<IConflictResult>(await manager.InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user2" }));
        Assert.True((await manager.InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user3" })).Success);
        Assert.IsAssignableFrom<IConflictResult>(await manager.InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user3" }));
        Assert.Single(await context.WorkspaceInvitations.ToListAsync());
    }

    [Fact]
    public async Task Expired_pending_invitation_is_closed_before_a_reinvite()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner));
        context.WorkspaceInvitations.Add(Invitation(workspaceId, 2, DateTime.UtcNow.AddMinutes(-1)));
        await context.SaveChangesAsync();

        var result = await Manager(context, 1).InviteAsync(workspaceId,
            new CreateWorkspaceInvitationDto { Username = "user2" });

        Assert.True(result.Success);
        var invitations = await context.WorkspaceInvitations.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, invitations.Count);
        Assert.Equal(WorkspaceInvitationStatus.Expired, invitations[0].Status);
        Assert.Equal(WorkspaceInvitationStatus.Pending, invitations[1].Status);
    }

    [Fact]
    public async Task My_invitations_returns_only_current_live_pending_invitations()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner));
        var live = Invitation(workspaceId, 2, DateTime.UtcNow.AddDays(1));
        var expired = Invitation(workspaceId, 2, DateTime.UtcNow.AddMinutes(-1));
        expired.Status = WorkspaceInvitationStatus.Expired;
        context.WorkspaceInvitations.AddRange(live, expired,
            Invitation(workspaceId, 3, DateTime.UtcNow.AddDays(1)));
        await context.SaveChangesAsync();

        var result = await Manager(context, 2).GetMyInvitationsAsync();

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal(2, result.Data![0].InvitedUserId);
        Assert.Equal(WorkspaceInvitationStatus.Pending, result.Data[0].Status);
    }

    [Fact]
    public async Task Only_invitee_can_accept_and_replay_does_not_duplicate_membership()
    {
        using var database = new TestDatabase();
        int workspaceId;
        int invitationId;
        await using (var setup = database.CreateContext())
        {
            workspaceId = await SeedWorkspace(setup, Member(1, WorkspaceRole.Owner));
            var invitation = Invitation(workspaceId, 2, DateTime.UtcNow.AddDays(1));
            setup.WorkspaceInvitations.Add(invitation);
            await setup.SaveChangesAsync();
            invitationId = invitation.Id;
        }

        await using (var unauthorized = database.CreateContext())
        {
            Assert.False((await Manager(unauthorized, 3).AcceptInvitationAsync(invitationId,
                new WorkspaceVersionDto())).Success);
        }

        await using (var accepting = database.CreateContext())
        {
            var manager = Manager(accepting, 2);
            Assert.True((await manager.AcceptInvitationAsync(invitationId, new WorkspaceVersionDto())).Success);
            Assert.True((await manager.AcceptInvitationAsync(invitationId, new WorkspaceVersionDto())).Success);
        }

        await using var verify = database.CreateContext();
        var membership = await verify.WorkspaceMembers.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == 2);
        Assert.Equal(WorkspaceRole.Member, membership.Role);
        Assert.True(membership.IsActive);
        Assert.Equal(2, await verify.WorkspaceMembers.CountAsync(x => x.WorkspaceId == workspaceId));
        Assert.Equal(1, await verify.WorkspaceActivities.CountAsync(x =>
            x.ActivityType == WorkspaceActivityType.InvitationAccepted));
    }

    [Fact]
    public async Task Accept_reactivates_the_same_membership_as_member_and_records_both_events()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var removed = Member(2, WorkspaceRole.Admin, active: false);
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner), removed);
        var originalMembershipId = removed.Id;
        var invitation = Invitation(workspaceId, 2, DateTime.UtcNow.AddDays(1));
        context.WorkspaceInvitations.Add(invitation);
        await context.SaveChangesAsync();

        var result = await Manager(context, 2).AcceptInvitationAsync(invitation.Id,
            new WorkspaceVersionDto { Version = invitation.Version });

        Assert.True(result.Success);
        var membership = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        Assert.Equal(originalMembershipId, membership.Id);
        Assert.True(membership.IsActive);
        Assert.Null(membership.RemovedAt);
        Assert.Equal(WorkspaceRole.Member, membership.Role);
        var activities = await context.WorkspaceActivities.Where(x => x.TargetUserId == 2)
            .Select(x => x.ActivityType).ToListAsync();
        Assert.Contains(WorkspaceActivityType.MemberReactivated, activities);
        Assert.Contains(WorkspaceActivityType.InvitationAccepted, activities);
    }

    [Fact]
    public async Task Expired_invitation_cannot_be_accepted_and_is_persisted_as_expired()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner));
        var invitation = Invitation(workspaceId, 2, DateTime.UtcNow.AddSeconds(-1));
        context.WorkspaceInvitations.Add(invitation);
        await context.SaveChangesAsync();

        var result = await Manager(context, 2).AcceptInvitationAsync(invitation.Id,
            new WorkspaceVersionDto { Version = invitation.Version });

        Assert.IsAssignableFrom<IConflictResult>(result);
        Assert.Equal(WorkspaceInvitationStatus.Expired,
            (await context.WorkspaceInvitations.SingleAsync()).Status);
        Assert.DoesNotContain(await context.WorkspaceMembers.ToListAsync(), x => x.UserId == 2);
    }

    [Fact]
    public async Task Invitee_can_reject_and_only_owner_or_admin_can_cancel()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner),
            Member(2, WorkspaceRole.Admin), Member(3));
        await AddUser(context, 4);
        await AddUser(context, 5);
        var rejected = Invitation(workspaceId, 4, DateTime.UtcNow.AddDays(1));
        var ownerCancelled = Invitation(workspaceId, 2, DateTime.UtcNow.AddDays(1));
        var adminCancelled = Invitation(workspaceId, 5, DateTime.UtcNow.AddDays(1));
        context.WorkspaceInvitations.AddRange(rejected, ownerCancelled, adminCancelled);
        await context.SaveChangesAsync();

        Assert.False((await Manager(context, 3).RejectInvitationAsync(rejected.Id,
            new WorkspaceVersionDto { Version = rejected.Version })).Success);
        Assert.True((await Manager(context, 4).RejectInvitationAsync(rejected.Id,
            new WorkspaceVersionDto { Version = rejected.Version })).Success);
        Assert.False((await Manager(context, 3).CancelInvitationAsync(workspaceId, ownerCancelled.Id,
            new WorkspaceVersionDto { Version = ownerCancelled.Version })).Success);
        Assert.True((await Manager(context, 1).CancelInvitationAsync(workspaceId, ownerCancelled.Id,
            new WorkspaceVersionDto { Version = ownerCancelled.Version })).Success);
        Assert.True((await Manager(context, 2).CancelInvitationAsync(workspaceId, adminCancelled.Id,
            new WorkspaceVersionDto { Version = adminCancelled.Version })).Success);
        Assert.Equal(WorkspaceInvitationStatus.Rejected, rejected.Status);
        Assert.Equal(WorkspaceInvitationStatus.Cancelled, ownerCancelled.Status);
        Assert.Equal(WorkspaceInvitationStatus.Cancelled, adminCancelled.Status);
    }

    [Fact]
    public async Task Removal_permission_matrix_soft_deletes_members_and_protects_owner()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner),
            Member(2, WorkspaceRole.Admin), Member(3), Member(4));
        var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        var ownerRemovedMember = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 4);
        var admin = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        var owner = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 1);

        Assert.True((await Manager(context, 2).RemoveMemberAsync(workspaceId, 3,
            new WorkspaceVersionDto { Version = member.Version })).Success);
        Assert.False((await Manager(context, 2).RemoveMemberAsync(workspaceId, 2,
            new WorkspaceVersionDto { Version = admin.Version })).Success);
        Assert.False((await Manager(context, 2).RemoveMemberAsync(workspaceId, 1,
            new WorkspaceVersionDto { Version = owner.Version })).Success);
        Assert.False((await Manager(context, 1).RemoveMemberAsync(workspaceId, 1,
            new WorkspaceVersionDto { Version = owner.Version })).Success);
        Assert.True((await Manager(context, 1).RemoveMemberAsync(workspaceId, 4,
            new WorkspaceVersionDto { Version = ownerRemovedMember.Version })).Success);
        Assert.True((await Manager(context, 1).RemoveMemberAsync(workspaceId, 2,
            new WorkspaceVersionDto { Version = admin.Version })).Success);

        Assert.Equal(4, await context.WorkspaceMembers.CountAsync());
        Assert.False(member.IsActive);
        Assert.NotNull(member.RemovedAt);
        Assert.False(ownerRemovedMember.IsActive);
        Assert.False(admin.IsActive);
        Assert.True(owner.IsActive);
    }

    [Fact]
    public async Task Only_owner_can_promote_demote_and_rename_with_current_versions()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner),
            Member(2, WorkspaceRole.Admin), Member(3), Member(4));
        var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        var owner = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 1);
        var workspace = await context.Workspaces.SingleAsync();

        Assert.False((await Manager(context, 2).ChangeMemberRoleAsync(workspaceId, 3,
            new ChangeWorkspaceMemberRoleDto { Role = WorkspaceRole.Admin, Version = member.Version })).Success);
        Assert.IsAssignableFrom<IConflictResult>(await Manager(context, 1).ChangeMemberRoleAsync(workspaceId, 3,
            new ChangeWorkspaceMemberRoleDto { Role = WorkspaceRole.Admin, Version = member.Version + 1 }));
        Assert.True((await Manager(context, 1).ChangeMemberRoleAsync(workspaceId, 3,
            new ChangeWorkspaceMemberRoleDto { Role = WorkspaceRole.Admin, Version = member.Version })).Success);
        Assert.True((await Manager(context, 1).ChangeMemberRoleAsync(workspaceId, 3,
            new ChangeWorkspaceMemberRoleDto { Role = WorkspaceRole.Member, Version = member.Version })).Success);
        Assert.False((await Manager(context, 1).ChangeMemberRoleAsync(workspaceId, 1,
            new ChangeWorkspaceMemberRoleDto { Role = WorkspaceRole.Member, Version = owner.Version })).Success);
        Assert.False((await Manager(context, 2).RenameAsync(workspaceId,
            new RenameWorkspaceDto { Name = "No", Version = workspace.Version })).Success);
        Assert.False((await Manager(context, 4).RenameAsync(workspaceId,
            new RenameWorkspaceDto { Name = "No", Version = workspace.Version })).Success);
        Assert.IsAssignableFrom<IConflictResult>(await Manager(context, 1).RenameAsync(workspaceId,
            new RenameWorkspaceDto { Name = "Stale", Version = workspace.Version + 1 }));
        Assert.True((await Manager(context, 1).RenameAsync(workspaceId,
            new RenameWorkspaceDto { Name = "Renamed", Version = workspace.Version })).Success);

        Assert.Equal("Renamed", workspace.Name);
        Assert.Equal(WorkspaceRole.Member, member.Role);
        var roleActivity = await context.WorkspaceActivities.SingleAsync(x =>
            x.ActivityType == WorkspaceActivityType.RolePromoted);
        Assert.Equal(WorkspaceRole.Member, roleActivity.FromRole);
        Assert.Equal(WorkspaceRole.Admin, roleActivity.ToRole);
        var demotionActivity = await context.WorkspaceActivities.SingleAsync(x =>
            x.ActivityType == WorkspaceActivityType.RoleDemoted);
        Assert.Equal(WorkspaceRole.Admin, demotionActivity.FromRole);
        Assert.Equal(WorkspaceRole.Member, demotionActivity.ToRole);
        Assert.Contains(await context.WorkspaceActivities.ToListAsync(), x =>
            x.ActivityType == WorkspaceActivityType.WorkspaceRenamed);
    }

    [Fact]
    public async Task Controller_maps_validation_authorization_hidden_resources_and_conflicts()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context, Member(1, WorkspaceRole.Owner), Member(2));
        var workspace = await context.Workspaces.SingleAsync();

        var invalid = await new WorkspacesController(Manager(context, 1))
            .Create(new CreateWorkspaceDto { Name = " " });
        var forbidden = await new WorkspacesController(Manager(context, 2))
            .Invite(workspaceId, new CreateWorkspaceInvitationDto { Username = "user3" });
        var hidden = await new WorkspacesController(Manager(context, 3)).GetDetails(workspaceId);
        var conflict = await new WorkspacesController(Manager(context, 1)).Rename(workspaceId,
            new RenameWorkspaceDto { Name = "Renamed", Version = workspace.Version + 1 });

        Assert.IsType<BadRequestObjectResult>(invalid);
        Assert.Equal(403, Assert.IsType<ObjectResult>(forbidden).StatusCode);
        Assert.IsType<NotFoundObjectResult>(hidden);
        Assert.IsType<ConflictObjectResult>(conflict);
    }

    private static WorkspaceManager Manager(TaskTrackerDbContext context, int userId) =>
        new(new UnitOfWork(context), new EfWorkspaceDal(context), new CurrentUser(userId),
            NullLogger<WorkspaceManager>.Instance);

    private static async Task<int> SeedWorkspace(TaskTrackerDbContext context, params WorkspaceMember[] members)
    {
        var workspace = new Workspace { Name = "Product", Members = members.ToList() };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        return workspace.Id;
    }

    private static WorkspaceMember Member(int userId, WorkspaceRole role = WorkspaceRole.Member,
        bool active = true) => new()
    {
        UserId = userId,
        Role = role,
        IsActive = active,
        RemovedAt = active ? null : DateTime.UtcNow
    };

    private static WorkspaceInvitation Invitation(int workspaceId, int invitedUserId, DateTime expiresAt) => new()
    {
        WorkspaceId = workspaceId,
        InvitedUserId = invitedUserId,
        InvitedByUserId = 1,
        Status = WorkspaceInvitationStatus.Pending,
        ExpiresAt = expiresAt
    };

    private static async Task AddUser(TaskTrackerDbContext context, int id)
    {
        context.Users.Add(new User
        {
            Id = id,
            FirstName = "Test",
            LastName = "User",
            UserName = $"user{id}",
            Email = $"user{id}@example.test",
            PasswordHash = [1],
            PasswordSalt = [1],
            Status = true
        });
        await context.SaveChangesAsync();
    }

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }
}
