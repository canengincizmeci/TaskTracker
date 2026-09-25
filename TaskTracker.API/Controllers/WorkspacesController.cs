using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using BusinessResult = TaskTracker.Core.Utilities.Results.IResult;

namespace TaskTracker.API.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces")]
public class WorkspacesController(IWorkspaceService workspaceService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkspaceDto dto)
    {
        var result = await workspaceService.CreateAsync(dto);
        return result.Success
            ? CreatedAtAction(nameof(GetDetails), new { workspaceId = result.Data.Id }, result.Data)
            : ToFailure(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        ToDataActionResult(await workspaceService.GetMineAsync());

    [HttpGet("{workspaceId:int}")]
    public async Task<IActionResult> GetDetails(int workspaceId) =>
        ToDataActionResult(await workspaceService.GetDetailsAsync(workspaceId));

    [HttpPut("{workspaceId:int}/rename")]
    public async Task<IActionResult> Rename(int workspaceId, RenameWorkspaceDto dto) =>
        ToActionResult(await workspaceService.RenameAsync(workspaceId, dto));

    [HttpGet("{workspaceId:int}/members")]
    public async Task<IActionResult> GetMembers(int workspaceId) =>
        ToDataActionResult(await workspaceService.GetMembersAsync(workspaceId));

    [HttpPost("{workspaceId:int}/invitations")]
    public async Task<IActionResult> Invite(int workspaceId, CreateWorkspaceInvitationDto dto) =>
        ToDataActionResult(await workspaceService.InviteAsync(workspaceId, dto));

    [HttpGet("{workspaceId:int}/invitations")]
    public async Task<IActionResult> GetInvitations(int workspaceId) =>
        ToDataActionResult(await workspaceService.GetInvitationsAsync(workspaceId));

    [HttpGet("invitations/my")]
    public async Task<IActionResult> GetMyInvitations() =>
        ToDataActionResult(await workspaceService.GetMyInvitationsAsync());

    [HttpPost("invitations/{invitationId:int}/accept")]
    public async Task<IActionResult> AcceptInvitation(int invitationId, WorkspaceVersionDto dto) =>
        ToActionResult(await workspaceService.AcceptInvitationAsync(invitationId, dto));

    [HttpPost("invitations/{invitationId:int}/reject")]
    public async Task<IActionResult> RejectInvitation(int invitationId, WorkspaceVersionDto dto) =>
        ToActionResult(await workspaceService.RejectInvitationAsync(invitationId, dto));

    [HttpPost("{workspaceId:int}/invitations/{invitationId:int}/cancel")]
    public async Task<IActionResult> CancelInvitation(int workspaceId, int invitationId, WorkspaceVersionDto dto) =>
        ToActionResult(await workspaceService.CancelInvitationAsync(workspaceId, invitationId, dto));

    [HttpPost("{workspaceId:int}/members/{userId:int}/remove")]
    public async Task<IActionResult> RemoveMember(int workspaceId, int userId, WorkspaceVersionDto dto) =>
        ToActionResult(await workspaceService.RemoveMemberAsync(workspaceId, userId, dto));

    [HttpPost("{workspaceId:int}/members/{userId:int}/role")]
    public async Task<IActionResult> ChangeMemberRole(int workspaceId, int userId, ChangeWorkspaceMemberRoleDto dto) =>
        ToActionResult(await workspaceService.ChangeMemberRoleAsync(workspaceId, userId, dto));

    private IActionResult ToActionResult(BusinessResult result) =>
        result.Success ? Ok(result.Message) : ToFailure(result);

    private IActionResult ToDataActionResult<T>(IDataResult<T> result) =>
        result.Success ? Ok(result.Data) : ToFailure(result);

    private IActionResult ToFailure(BusinessResult result) => result switch
    {
        IConflictResult => Conflict(new { code = "conflict", message = result.Message }),
        _ when result.Message == Messages.AuthorizationDenied => StatusCode(403, result.Message),
        _ when result.Message == WorkspaceMessages.NotFound ||
            result.Message == WorkspaceMessages.MemberNotFound ||
            result.Message == WorkspaceMessages.InvitationNotFound ||
            result.Message == Messages.UserNotFound => NotFound(result.Message),
        _ => BadRequest(result.Message)
    };
}
