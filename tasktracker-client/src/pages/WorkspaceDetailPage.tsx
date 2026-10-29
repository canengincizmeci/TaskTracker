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

function invitationDisplayStatus(invitation: WorkspaceInvitation) {
  return invitation.status === "Pending" && invitation.expiresAt &&
    new Date(invitation.expiresAt).getTime() <= Date.now()
    ? "Expired"
    : invitation.status;
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
  const currentPendingInvitations = currentInvitations.filter(isPendingAndCurrent);
  const currentInvitationHistory = currentInvitations.filter((invitation) => !isPendingAndCurrent(invitation));
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
            <header id="workspace-overview" className="workspace-detail-header">
              <div className="workspace-detail-heading">
                <p className="eyebrow">Workspace</p>
                <div className="workspace-detail-title"><h1>{workspace.name}</h1></div>
                <div className="workspace-detail-meta" aria-label="Workspace details">
                  <span className={`workspace-role workspace-role--${workspace.currentUserRole.toLowerCase()}`}>
                    {workspace.currentUserRole}</span>
                  <span>{workspace.memberCount} {workspace.memberCount === 1 ? "member" : "members"}</span>
                  <span>Created {formatDate(workspace.createdAt)}</span>
                </div>
              </div>
              <div className="workspace-detail-header__actions">
                {canManageInvitations && <a className="primary-button" href="#workspace-invitations">Invite member</a>}
                <a className="secondary-button"
                  href={workspace.currentUserRole === "Owner" ? "#workspace-settings" : "#workspace-people"}>
                  {workspace.currentUserRole === "Owner" ? "Workspace settings" : "View people"}
                </a>
              </div>
            </header>

            <nav className="workspace-local-nav" aria-label="Workspace sections">
              <a href="#workspace-overview">Overview</a>
              <a href="#workspace-tasks">Tasks</a>
              <a href="#workspace-people">People</a>
              {workspace.currentUserRole === "Owner" && <a href="#workspace-settings">Settings</a>}
            </nav>

            <WorkspaceTasksSection key={workspace.id} workspace={workspace} />

            <section id="workspace-people" className="workspace-product-section workspace-people-section"
              aria-labelledby="workspace-people-title">
              <div className="workspace-section-heading">
                <div>
                  <p className="eyebrow">Collaboration</p>
                  <h2 id="workspace-people-title">People</h2>
                  <p>{canManageWorkspace ? "Review members and manage access where permitted." :
                    "People who currently belong to this workspace."}</p>
                </div>
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
                      <div className="workspace-member__identity">
                        <h3>{member.userName}</h3><p>Joined {formatDate(member.joinedAt)}</p>
                      </div>
                      <div className="workspace-member__controls">
                        <span className={`workspace-role workspace-role--${member.role.toLowerCase()}`}>{member.role}</span>
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

              {canManageInvitations && (
                <section id="workspace-invitations" className="workspace-people-invitations"
                  aria-labelledby="workspace-management-invitations-title">
                  <div className="workspace-management-section__heading">
                    <div><h3 id="workspace-management-invitations-title">Invite members</h3>
                      <p>Invite someone by username. New members join with the Member role.</p></div>
                    {!currentInvitationsLoading && !currentInvitationFailure &&
                      <span>{currentPendingInvitations.length} pending</span>}
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
                    <div className="workspace-invitation-groups">
                      {[
                        { label: "Pending", items: currentPendingInvitations, history: false },
                        { label: "History", items: currentInvitationHistory, history: true },
                      ].filter((group) => group.items.length > 0).map((group) => (
                        <section className={`workspace-invitation-group${group.history ? " workspace-invitation-group--history" : ""}`}
                          aria-labelledby={`workspace-management-invitation-group-${group.label.toLowerCase()}`}
                          key={group.label}>
                          <h4 id={`workspace-management-invitation-group-${group.label.toLowerCase()}`}>{group.label}</h4>
                          <div className="workspace-invitation-list">
                            {group.items.map((invitation) => {
                              const cancelBusy = busyOperations.has(`invitation:${invitation.id}`);
                              const canCancel = isPendingAndCurrent(invitation);
                              const displayStatus = invitationDisplayStatus(invitation);
                              return (
                                <article className="workspace-invitation-row" key={invitation.id}>
                                  <div><div className="workspace-invitation-row__title">
                                    <h5>{invitation.invitedUserName}</h5>
                                    <span className={`workspace-invitation-status workspace-invitation-status--${displayStatus.toLowerCase()}`}>
                                      {displayStatus}</span>
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
                        </section>
                      ))}
                    </div>
                  )}
                </section>
              )}
            </section>

            {workspace.currentUserRole === "Owner" && (
              <section id="workspace-settings" className="workspace-product-section workspace-settings-section"
                aria-labelledby="workspace-settings-title">
                <div className="workspace-section-heading">
                  <div><p className="eyebrow">Owner controls</p>
                    <h2 id="workspace-settings-title">Settings</h2>
                    <p>Manage workspace-level details.</p></div>
                </div>
                <div className="workspace-settings-row" aria-labelledby="workspace-name-title">
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
              </section>
            )}
          </>
        )}
      </div>
    </main>
  );
}
