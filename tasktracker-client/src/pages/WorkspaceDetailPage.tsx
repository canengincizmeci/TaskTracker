import { isAxiosError } from "axios";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import {
  cancelWorkspaceInvitation,
  changeWorkspaceMemberRole,
  getWorkspace,
  getWorkspaceInvitations,
  inviteWorkspaceMember,
  removeWorkspaceMember,
  renameWorkspace,
} from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "../components/LoadingSpinner";
import WorkspaceTasksSection from "../components/WorkspaceTasksSection";
import type {
  WorkspaceDetail,
  WorkspaceInvitation,
  WorkspaceMember,
  WorkspaceRole,
} from "../types/workspace";
import "../styles/workspaces.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function responseStatus(error: unknown) {
  return isAxiosError(error) ? error.response?.status : undefined;
}

function canRemoveMember(actorRole: WorkspaceRole, memberRole: WorkspaceRole) {
  if (memberRole === "Owner") return false;
  return actorRole === "Owner" || (actorRole === "Admin" && memberRole === "Member");
}

function isPendingAndCurrent(invitation: WorkspaceInvitation) {
  return invitation.status === "Pending" &&
    (!invitation.expiresAt || new Date(invitation.expiresAt).getTime() > Date.now());
}

export default function WorkspaceDetailPage() {
  const { workspaceId } = useParams();
  const parsedWorkspaceId = Number(workspaceId);
  const validWorkspaceId = Number.isSafeInteger(parsedWorkspaceId) && parsedWorkspaceId > 0;
  const [workspace, setWorkspace] = useState<WorkspaceDetail | null>(null);
  const [loading, setLoading] = useState(validWorkspaceId);
  const [failure, setFailure] = useState(validWorkspaceId ? "" : "This workspace address is invalid.");
  const [failureWorkspaceId, setFailureWorkspaceId] = useState(parsedWorkspaceId);
  const workspaceRequestId = useRef(0);
  const [editingName, setEditingName] = useState(false);
  const [workspaceName, setWorkspaceName] = useState("");
  const [renameFailure, setRenameFailure] = useState("");
  const [renameNotice, setRenameNotice] = useState("");
  const renameInProgress = useRef(false);
  const [renaming, setRenaming] = useState(false);

  const [invitations, setInvitations] = useState<WorkspaceInvitation[]>([]);
  const [invitationsLoading, setInvitationsLoading] = useState(false);
  const [invitationFailure, setInvitationFailure] = useState("");
  const [invitationNotice, setInvitationNotice] = useState("");
  const [invitationsWorkspaceId, setInvitationsWorkspaceId] = useState<number | null>(null);
  const invitationsWorkspaceIdRef = useRef<number | null>(null);
  const invitationRequestId = useRef(0);
  const [username, setUsername] = useState("");
  const inviteInProgress = useRef(false);
  const [inviting, setInviting] = useState(false);

  const activeOperations = useRef(new Set<string>());
  const [busyOperations, setBusyOperations] = useState<Set<string>>(new Set());
  const [memberFailure, setMemberFailure] = useState("");
  const [memberNotice, setMemberNotice] = useState("");
  const [managementOpen, setManagementOpen] = useState(false);
  const routeVersion = useRef(0);
  const currentRouteWorkspaceId = useRef(parsedWorkspaceId);

  useLayoutEffect(() => {
    if (currentRouteWorkspaceId.current === parsedWorkspaceId) return;
    currentRouteWorkspaceId.current = parsedWorkspaceId;
    routeVersion.current++;
    workspaceRequestId.current++;
    invitationRequestId.current++;
  }, [parsedWorkspaceId]);

  const invalidateRequests = useCallback(() => {
    workspaceRequestId.current++;
    invitationRequestId.current++;
  }, []);

  const loadWorkspace = useCallback(async (initial = true) => {
    if (!validWorkspaceId) return false;

    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrentRoute = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;
    if (!isCurrentRoute()) return false;
    const currentRequest = ++workspaceRequestId.current;
    const isCurrent = () => isCurrentRoute() && currentRequest === workspaceRequestId.current;
    if (initial && isCurrent()) setLoading(true);
    setFailureWorkspaceId(requestedWorkspaceId);
    setFailure("");

    try {
      const data = await getWorkspace(requestedWorkspaceId);
      if (isCurrent()) setWorkspace(data);
      return isCurrent();
    } catch (reason: unknown) {
      if (isCurrent()) {
        const status = responseStatus(reason);
        setFailure(status === 403
          ? "You do not have access to this workspace."
          : status === 404
            ? "This workspace could not be found."
            : errorMessage(reason, "This workspace could not be loaded."));
      }
      return false;
    } finally {
      if (initial && isCurrent()) setLoading(false);
    }
  }, [parsedWorkspaceId, validWorkspaceId]);

  const loadInvitations = useCallback(async (initial = true) => {
    if (!validWorkspaceId) return false;

    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrentRoute = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;
    if (!isCurrentRoute()) return false;
    const currentRequest = ++invitationRequestId.current;
    const isCurrent = () => isCurrentRoute() && currentRequest === invitationRequestId.current;
    if (initial && isCurrent()) setInvitationsLoading(true);
    const previousWorkspaceId = invitationsWorkspaceIdRef.current;
    invitationsWorkspaceIdRef.current = requestedWorkspaceId;
    setInvitationsWorkspaceId(requestedWorkspaceId);
    setInvitations((current) => previousWorkspaceId === requestedWorkspaceId ? current : []);
    setInvitationFailure("");

    try {
      const data = await getWorkspaceInvitations(requestedWorkspaceId);
      if (isCurrent()) setInvitations(data);
      return isCurrent();
    } catch (reason: unknown) {
      if (isCurrent()) {
        setInvitationFailure(errorMessage(reason, "Workspace invitations could not be loaded."));
      }
      return false;
    } finally {
      if (initial && isCurrent()) setInvitationsLoading(false);
    }
  }, [parsedWorkspaceId, validWorkspaceId]);

  useEffect(() => {
    const request = window.setTimeout(() => {
      setEditingName(false);
      setWorkspaceName("");
      setRenameFailure("");
      setRenameNotice("");
      renameInProgress.current = false;
      setRenaming(false);
      setInvitations([]);
      invitationsWorkspaceIdRef.current = null;
      setInvitationsWorkspaceId(null);
      setInvitationsLoading(false);
      setInvitationFailure("");
      setInvitationNotice("");
      setUsername("");
      inviteInProgress.current = false;
      setInviting(false);
      activeOperations.current.clear();
      setBusyOperations(new Set());
      setMemberFailure("");
      setMemberNotice("");
      setManagementOpen(false);
      void loadWorkspace();
    }, 0);

    return () => {
      window.clearTimeout(request);
      invalidateRequests();
    };
  }, [invalidateRequests, loadWorkspace]);

  const hasCurrentWorkspace = workspace?.id === parsedWorkspaceId;
  const canManageInvitations = workspace?.currentUserRole === "Owner" ||
    workspace?.currentUserRole === "Admin";
  const canManageWorkspace = canManageInvitations;
  const currentFailure = failureWorkspaceId === parsedWorkspaceId ? failure : "";
  const invitationsBelongToCurrentWorkspace = invitationsWorkspaceId === parsedWorkspaceId;
  const currentInvitations = invitationsBelongToCurrentWorkspace ? invitations : [];
  const currentInvitationFailure = invitationsBelongToCurrentWorkspace ? invitationFailure : "";
  const currentInvitationsLoading = invitationsLoading || !invitationsBelongToCurrentWorkspace;

  useEffect(() => {
    if (!hasCurrentWorkspace || !canManageInvitations) return;

    const request = window.setTimeout(() => {
      void loadInvitations();
    }, 0);

    return () => window.clearTimeout(request);
  }, [canManageInvitations, hasCurrentWorkspace, loadInvitations]);

  const beginOperation = (key: string) => {
    if (activeOperations.current.has(key)) return false;
    activeOperations.current.add(key);
    setBusyOperations(new Set(activeOperations.current));
    return true;
  };

  const finishOperation = (key: string) => {
    activeOperations.current.delete(key);
    setBusyOperations(new Set(activeOperations.current));
  };

  const beginRename = () => {
    if (!workspace || workspace.currentUserRole !== "Owner") return;
    setWorkspaceName(workspace.name);
    setRenameFailure("");
    setRenameNotice("");
    setEditingName(true);
  };

  const cancelRename = () => {
    if (renaming) return;
    setWorkspaceName(workspace?.name ?? "");
    setRenameFailure("");
    setEditingName(false);
  };

  const handleRename = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!workspace || workspace.currentUserRole !== "Owner" || renameInProgress.current) return;
    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrent = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;

    const normalizedName = workspaceName.trim();
    if (!normalizedName) {
      setRenameFailure("Workspace name is required.");
      return;
    }
    if (normalizedName.length > 150) {
      setRenameFailure("Workspace name must be 150 characters or fewer.");
      return;
    }
    if (normalizedName === workspace.name) {
      setRenameFailure("");
      setEditingName(false);
      return;
    }

    renameInProgress.current = true;
    setRenaming(true);
    setRenameFailure("");
    setRenameNotice("");

    try {
      const message = await renameWorkspace(requestedWorkspaceId, normalizedName, workspace.version);
      if (!isCurrent()) return;
      setWorkspace((current) => current?.id === requestedWorkspaceId
        ? { ...current, name: normalizedName }
        : current);
      setEditingName(false);
      const refreshed = await loadWorkspace(false);
      if (!isCurrent()) return;
      if (refreshed) {
        setRenameNotice(message || "Workspace renamed.");
      } else {
        setRenameFailure("The workspace was renamed, but its latest details could not be loaded.");
      }
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const status = responseStatus(reason);
      const message = errorMessage(reason, "The workspace could not be renamed.");
      if (status === 409 || status === 403 || status === 404) {
        const refreshed = await loadWorkspace(false);
        if (!isCurrent()) return;
        if (status === 409) {
          setRenameFailure(`${message} ${refreshed
            ? "Latest workspace data was loaded."
            : "Refresh and try again."}`);
        } else {
          setRenameFailure(message);
        }
        setEditingName(false);
      } else {
        setRenameFailure(message);
      }
    } finally {
      if (isCurrent()) {
        renameInProgress.current = false;
        setRenaming(false);
      }
    }
  };

  const handleInvite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedUsername = username.trim();
    if (!normalizedUsername || inviteInProgress.current) return;
    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrent = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;

    inviteInProgress.current = true;
    setInviting(true);
    setInvitationFailure("");
    setInvitationNotice("");

    try {
      const invitation = await inviteWorkspaceMember(requestedWorkspaceId, normalizedUsername);
      if (!isCurrent()) return;
      invitationsWorkspaceIdRef.current = requestedWorkspaceId;
      setInvitationsWorkspaceId(requestedWorkspaceId);
      setInvitations((current) => [invitation, ...current]);
      setUsername("");
      setInvitationNotice(`Invitation sent to ${invitation.invitedUserName}.`);
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const message = errorMessage(reason, "The invitation could not be sent.");
      if (responseStatus(reason) === 409) {
        const [workspaceRefreshed, invitationsRefreshed] = await Promise.all([
          loadWorkspace(false),
          loadInvitations(false),
        ]);
        if (!isCurrent()) return;
        setInvitationFailure(`${message} ${workspaceRefreshed && invitationsRefreshed
          ? "Latest workspace data was loaded."
          : "Refresh and try again."}`);
      } else {
        setInvitationFailure(message);
      }
    } finally {
      if (isCurrent()) {
        inviteInProgress.current = false;
        setInviting(false);
      }
    }
  };

  const handleCancelInvitation = async (invitation: WorkspaceInvitation) => {
    if (!window.confirm(`Cancel the invitation for ${invitation.invitedUserName}?`)) return;
    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrent = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;
    const key = `invitation:${invitation.id}`;
    if (!beginOperation(key)) return;

    setInvitationFailure("");
    setInvitationNotice("");

    try {
      await cancelWorkspaceInvitation(requestedWorkspaceId, invitation.id, invitation.version);
      if (!isCurrent()) return;
      setInvitationNotice(`Invitation for ${invitation.invitedUserName} cancelled.`);
      await loadInvitations(false);
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const message = errorMessage(reason, "The invitation could not be cancelled.");
      if (responseStatus(reason) === 409) {
        const refreshed = await loadInvitations(false);
        if (!isCurrent()) return;
        setInvitationFailure(`${message} ${refreshed ? "Latest invitations were loaded." : "Refresh and try again."}`);
      } else {
        setInvitationFailure(message);
      }
    } finally {
      if (isCurrent()) finishOperation(key);
    }
  };

  const handleRoleChange = async (member: WorkspaceMember) => {
    const nextRole = member.role === "Member" ? "Admin" : "Member";
    const action = nextRole === "Admin" ? "promote" : "demote";
    if (!window.confirm(`${action === "promote" ? "Promote" : "Demote"} ${member.userName} ${action === "promote" ? "to Admin" : "to Member"}?`)) return;
    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrent = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;

    const key = `member:${member.userId}`;
    if (!beginOperation(key)) return;
    setMemberFailure("");
    setMemberNotice("");

    try {
      await changeWorkspaceMemberRole(requestedWorkspaceId, member.userId, nextRole, member.version);
      if (!isCurrent()) return;
      setMemberNotice(`${member.userName} is now ${nextRole}.`);
      await loadWorkspace(false);
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const message = errorMessage(reason, "The member role could not be changed.");
      const stale = responseStatus(reason) === 409;
      if (stale || responseStatus(reason) === 403) {
        const refreshed = await loadWorkspace(false);
        if (!isCurrent()) return;
        setMemberFailure(`${message}${stale
          ? ` ${refreshed ? "Latest member data was loaded." : "Refresh and try again."}`
          : ""}`);
      } else {
        setMemberFailure(message);
      }
    } finally {
      if (isCurrent()) finishOperation(key);
    }
  };

  const handleRemoveMember = async (member: WorkspaceMember) => {
    if (!window.confirm(`Remove ${member.userName} from this workspace?`)) return;
    const requestedWorkspaceId = parsedWorkspaceId;
    const route = routeVersion.current;
    const isCurrent = () => currentRouteWorkspaceId.current === requestedWorkspaceId &&
      routeVersion.current === route;
    const key = `member:${member.userId}`;
    if (!beginOperation(key)) return;
    setMemberFailure("");
    setMemberNotice("");

    try {
      await removeWorkspaceMember(requestedWorkspaceId, member.userId, member.version);
      if (!isCurrent()) return;
      setMemberNotice(`${member.userName} was removed from the workspace.`);
      await loadWorkspace(false);
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const message = errorMessage(reason,
        "This member could not be removed. They may still own or be assigned to active workspace tasks.");
      const stale = responseStatus(reason) === 409;
      if (stale || responseStatus(reason) === 403) {
        const refreshed = await loadWorkspace(false);
        if (!isCurrent()) return;
        setMemberFailure(`${message}${stale
          ? ` ${refreshed ? "Latest member data was loaded." : "Refresh and try again."}`
          : ""}`);
      } else {
        setMemberFailure(message);
      }
    } finally {
      if (isCurrent()) finishOperation(key);
    }
  };

  return (
    <main className="page workspace-page">
      <div className="workspace-shell">
        <Link className="workspace-back-link" to="/workspaces">← Back to workspaces</Link>

        {!validWorkspaceId ? (
          <div className="workspace-detail-state workspace-state--error" role="alert">
            <h2>Workspace unavailable</h2>
            <p>This workspace address is invalid.</p>
          </div>
        ) : loading || (!currentFailure && !hasCurrentWorkspace) ? (
          <div className="workspace-detail-state"><LoadingSpinner text="Loading workspace..." /></div>
        ) : currentFailure || !workspace || !hasCurrentWorkspace ? (
          <div className="workspace-detail-state workspace-state--error" role="alert">
            <h2>Workspace unavailable</h2>
            <p>{currentFailure || "This workspace could not be found."}</p>
            <button className="secondary-button" onClick={() => void loadWorkspace()} type="button">
              Try again
            </button>
          </div>
        ) : (
          <>
            <header className="workspace-detail-header">
              <div>
                <p className="eyebrow">Workspace</p>
                <div className="workspace-detail-title"><h1>{workspace.name}</h1></div>
                <p className="workspace-detail-meta">Created {formatDate(workspace.createdAt)}</p>
              </div>
              <div className="workspace-detail-header__actions">
                <div className="workspace-detail-summary">
                  <div><span>Your role</span><strong>{workspace.currentUserRole}</strong></div>
                  <div><span>People</span><strong>{workspace.memberCount}</strong></div>
                </div>
                {canManageWorkspace && (
                  <button className="secondary-button" type="button" aria-expanded={managementOpen}
                    aria-controls="workspace-management-panel"
                    onClick={() => setManagementOpen((current) => !current)}>
                    {managementOpen ? "Close management" : "Manage workspace"}
                  </button>
                )}
              </div>
            </header>

            <WorkspaceTasksSection key={workspace.id} workspace={workspace} />

            <section className="workspace-detail-card workspace-people-summary"
              aria-labelledby="workspace-people-title">
              <div>
                <p className="eyebrow">People</p>
                <h2 id="workspace-people-title">{workspace.memberCount} {workspace.memberCount === 1 ? "member" : "members"}</h2>
                <p>{workspace.memberCount === 1
                  ? "Only the workspace owner is here so far."
                  : "The people who can collaborate on this workspace's tasks."}</p>
                <div className="workspace-people-preview" aria-label="Workspace members">
                  {workspace.members.slice(0, 3).map((member) => (
                    <span key={member.userId}>{member.userName}</span>
                  ))}
                  {workspace.memberCount > 3 && <span>+{workspace.memberCount - 3} more</span>}
                </div>
              </div>
              <button className="secondary-button" type="button" aria-expanded={managementOpen}
                aria-controls="workspace-management-panel"
                onClick={() => setManagementOpen((current) => !current)}>
                {managementOpen ? "Hide people" : canManageWorkspace ? "Manage people" : "View people"}
              </button>
            </section>

            <section id="workspace-management-panel" className="workspace-detail-card workspace-management-card"
              aria-labelledby="workspace-management-title" hidden={!managementOpen}>
              <div className="workspace-section-heading">
                <div>
                  <p className="eyebrow">{canManageWorkspace ? "Management" : "People"}</p>
                  <h2 id="workspace-management-title">{canManageWorkspace ? "Manage workspace" : "Workspace people"}</h2>
                </div>
                <button className="workspace-text-button" type="button"
                  onClick={() => setManagementOpen(false)}>Close</button>
              </div>

              {workspace.currentUserRole === "Owner" && (
                <div className="workspace-management-section" aria-labelledby="workspace-name-title">
                  <div className="workspace-management-section__heading">
                    <div><h3 id="workspace-name-title">Workspace name</h3>
                      <p>Keep the workspace name clear for everyone.</p></div>
                    {!editingName && <button className="secondary-button" onClick={beginRename}
                      type="button">Rename workspace</button>}
                  </div>
                  {editingName && (
                    <form className="workspace-rename-form" onSubmit={handleRename}>
                      <label htmlFor="workspace-rename-name">Workspace name</label>
                      <input id="workspace-rename-name" autoFocus disabled={renaming} maxLength={150}
                        onChange={(event) => {
                          setWorkspaceName(event.target.value);
                          if (renameFailure) setRenameFailure("");
                        }} value={workspaceName} />
                      <div className="workspace-rename-actions">
                        <button className="primary-button" disabled={renaming || !workspaceName.trim()}
                          type="submit">{renaming ? "Saving..." : "Save name"}</button>
                        <button className="secondary-button" disabled={renaming} onClick={cancelRename}
                          type="button">Cancel</button>
                      </div>
                    </form>
                  )}
                  {renameFailure && <p className="workspace-rename-message workspace-rename-message--error"
                    role="alert">{renameFailure}</p>}
                  {renameNotice && <p className="workspace-rename-message" role="status">{renameNotice}</p>}
                </div>
              )}

              <div className="workspace-management-section" aria-labelledby="workspace-members-title">
                <div className="workspace-management-section__heading">
                  <div><h3 id="workspace-members-title">People</h3>
                    <p>{canManageWorkspace ? "Review members and update access where permitted." :
                      "People who currently belong to this workspace."}</p></div>
                  <span>{workspace.memberCount} total</span>
                </div>
                {memberFailure && <p className="workspace-section-alert" role="alert">{memberFailure}</p>}
                {memberNotice && <p className="workspace-section-notice" role="status">{memberNotice}</p>}
                <div className="workspace-member-list">
                  {workspace.members.map((member) => {
                    const memberBusy = busyOperations.has(`member:${member.userId}`);
                    const canChangeRole = workspace.currentUserRole === "Owner" && member.role !== "Owner";
                    const canRemove = canRemoveMember(workspace.currentUserRole, member.role);
                    return (
                      <article className="workspace-member" key={member.userId}>
                        <div className="workspace-member__avatar" aria-hidden="true">
                          {member.userName.trim().charAt(0).toUpperCase() || "?"}
                        </div>
                        <div><h3>{member.userName}</h3><p>Joined {formatDate(member.joinedAt)}</p></div>
                        <div className="workspace-member__controls">
                          <span className="workspace-role">{member.role}</span>
                          {(canChangeRole || canRemove) && (
                            <div className="workspace-row-actions">
                              {canChangeRole && <button className="workspace-text-button" disabled={memberBusy}
                                onClick={() => void handleRoleChange(member)} type="button">
                                {member.role === "Member" ? "Change to Admin" : "Change to Member"}
                              </button>}
                              {canRemove && <button className="workspace-text-button workspace-text-button--danger"
                                disabled={memberBusy} onClick={() => void handleRemoveMember(member)} type="button">
                                {memberBusy ? "Working..." : "Remove member"}
                              </button>}
                            </div>
                          )}
                        </div>
                      </article>
                    );
                  })}
                </div>
              </div>

              {canManageInvitations && (
                <div className="workspace-management-section" aria-labelledby="workspace-management-invitations-title">
                  <div className="workspace-management-section__heading">
                    <div><h3 id="workspace-management-invitations-title">Invite members</h3>
                      <p>Invite someone by username. New members join with the Member role.</p></div>
                    {!currentInvitationsLoading && !currentInvitationFailure &&
                      <span>{currentInvitations.filter(isPendingAndCurrent).length} pending</span>}
                  </div>
                  <form className="workspace-invite-form" onSubmit={handleInvite}>
                    <label htmlFor="workspace-invite-username">Username</label>
                    <div>
                      <input id="workspace-invite-username" value={username}
                        onChange={(event) => {
                          setUsername(event.target.value);
                          if (currentInvitationFailure) setInvitationFailure("");
                        }} placeholder="Username" disabled={inviting} required />
                      <button className="primary-button" disabled={!username.trim() || inviting} type="submit">
                        {inviting ? "Sending..." : "Invite member"}
                      </button>
                    </div>
                  </form>
                  {currentInvitationFailure && <p className="workspace-section-alert"
                    role="alert">{currentInvitationFailure}</p>}
                  {invitationNotice && <p className="workspace-section-notice" role="status">{invitationNotice}</p>}
                  {currentInvitationsLoading ? (
                    <div className="workspace-compact-state"><LoadingSpinner text="Loading invitations..." /></div>
                  ) : currentInvitationFailure && currentInvitations.length === 0 ? (
                    <div className="workspace-compact-state"><button className="secondary-button"
                      onClick={() => void loadInvitations()} type="button">Try again</button></div>
                  ) : currentInvitations.length === 0 ? (
                    <div className="workspace-empty-row"><strong>No pending invitations</strong>
                      <span>Invite a member when you are ready to grow this workspace.</span></div>
                  ) : (
                    <div className="workspace-invitation-list">
                      {currentInvitations.map((invitation) => {
                        const cancelBusy = busyOperations.has(`invitation:${invitation.id}`);
                        const canCancel = isPendingAndCurrent(invitation);
                        return (
                          <article className="workspace-invitation-row" key={invitation.id}>
                            <div><div className="workspace-invitation-row__title">
                              <h3>{invitation.invitedUserName}</h3>
                              <span className={`workspace-invitation-status workspace-invitation-status--${invitation.status.toLowerCase()}`}>
                                {invitation.status}</span>
                            </div><p>Invited {formatDate(invitation.createdAt)}</p>
                              {invitation.expiresAt && <p>Expires {formatDate(invitation.expiresAt)}</p>}</div>
                            {canCancel && <button className="secondary-button" disabled={cancelBusy}
                              onClick={() => void handleCancelInvitation(invitation)} type="button">
                              {cancelBusy ? "Cancelling..." : "Cancel invitation"}
                            </button>}
                          </article>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}
            </section>
          </>
        )}
      </div>
    </main>
  );
}
