import { isAxiosError } from "axios";
import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import {
  cancelWorkspaceInvitation,
  changeWorkspaceMemberRole,
  getWorkspace,
  getWorkspaceInvitations,
  inviteWorkspaceMember,
  removeWorkspaceMember,
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
  const workspaceRequestId = useRef(0);

  const [invitations, setInvitations] = useState<WorkspaceInvitation[]>([]);
  const [invitationsLoading, setInvitationsLoading] = useState(false);
  const [invitationFailure, setInvitationFailure] = useState("");
  const [invitationNotice, setInvitationNotice] = useState("");
  const invitationRequestId = useRef(0);
  const [username, setUsername] = useState("");
  const inviteInProgress = useRef(false);
  const [inviting, setInviting] = useState(false);

  const activeOperations = useRef(new Set<string>());
  const [busyOperations, setBusyOperations] = useState<Set<string>>(new Set());
  const [memberFailure, setMemberFailure] = useState("");
  const [memberNotice, setMemberNotice] = useState("");

  const invalidateRequests = useCallback(() => {
    workspaceRequestId.current++;
    invitationRequestId.current++;
  }, []);

  const loadWorkspace = useCallback(async (initial = true) => {
    if (!validWorkspaceId) return false;

    const currentRequest = ++workspaceRequestId.current;
    if (initial) setLoading(true);
    setFailure("");

    try {
      const data = await getWorkspace(parsedWorkspaceId);
      if (currentRequest === workspaceRequestId.current) setWorkspace(data);
      return true;
    } catch (reason: unknown) {
      if (currentRequest === workspaceRequestId.current) {
        setFailure(errorMessage(reason, "This workspace could not be loaded."));
      }
      return false;
    } finally {
      if (initial && currentRequest === workspaceRequestId.current) setLoading(false);
    }
  }, [parsedWorkspaceId, validWorkspaceId]);

  const loadInvitations = useCallback(async (initial = true) => {
    if (!validWorkspaceId) return false;

    const currentRequest = ++invitationRequestId.current;
    if (initial) setInvitationsLoading(true);
    setInvitationFailure("");

    try {
      const data = await getWorkspaceInvitations(parsedWorkspaceId);
      if (currentRequest === invitationRequestId.current) setInvitations(data);
      return true;
    } catch (reason: unknown) {
      if (currentRequest === invitationRequestId.current) {
        setInvitationFailure(errorMessage(reason, "Workspace invitations could not be loaded."));
      }
      return false;
    } finally {
      if (initial && currentRequest === invitationRequestId.current) setInvitationsLoading(false);
    }
  }, [parsedWorkspaceId, validWorkspaceId]);

  useEffect(() => {
    const request = window.setTimeout(() => {
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

  const handleInvite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedUsername = username.trim();
    if (!normalizedUsername || inviteInProgress.current) return;

    inviteInProgress.current = true;
    setInviting(true);
    setInvitationFailure("");
    setInvitationNotice("");

    try {
      const invitation = await inviteWorkspaceMember(parsedWorkspaceId, normalizedUsername);
      setInvitations((current) => [invitation, ...current]);
      setUsername("");
      setInvitationNotice(`Invitation sent to ${invitation.invitedUserName}.`);
    } catch (reason: unknown) {
      const message = errorMessage(reason, "The invitation could not be sent.");
      if (responseStatus(reason) === 409) {
        const [workspaceRefreshed, invitationsRefreshed] = await Promise.all([
          loadWorkspace(false),
          loadInvitations(false),
        ]);
        setInvitationFailure(`${message} ${workspaceRefreshed && invitationsRefreshed
          ? "Latest workspace data was loaded."
          : "Refresh and try again."}`);
      } else {
        setInvitationFailure(message);
      }
    } finally {
      inviteInProgress.current = false;
      setInviting(false);
    }
  };

  const handleCancelInvitation = async (invitation: WorkspaceInvitation) => {
    if (!window.confirm(`Cancel the invitation for ${invitation.invitedUserName}?`)) return;
    const key = `invitation:${invitation.id}`;
    if (!beginOperation(key)) return;

    setInvitationFailure("");
    setInvitationNotice("");

    try {
      await cancelWorkspaceInvitation(parsedWorkspaceId, invitation.id, invitation.version);
      setInvitationNotice(`Invitation for ${invitation.invitedUserName} cancelled.`);
      await loadInvitations(false);
    } catch (reason: unknown) {
      const message = errorMessage(reason, "The invitation could not be cancelled.");
      if (responseStatus(reason) === 409) {
        const refreshed = await loadInvitations(false);
        setInvitationFailure(`${message} ${refreshed ? "Latest invitations were loaded." : "Refresh and try again."}`);
      } else {
        setInvitationFailure(message);
      }
    } finally {
      finishOperation(key);
    }
  };

  const handleRoleChange = async (member: WorkspaceMember) => {
    const nextRole = member.role === "Member" ? "Admin" : "Member";
    const action = nextRole === "Admin" ? "promote" : "demote";
    if (!window.confirm(`${action === "promote" ? "Promote" : "Demote"} ${member.userName} ${action === "promote" ? "to Admin" : "to Member"}?`)) return;

    const key = `member:${member.userId}`;
    if (!beginOperation(key)) return;
    setMemberFailure("");
    setMemberNotice("");

    try {
      await changeWorkspaceMemberRole(parsedWorkspaceId, member.userId, nextRole, member.version);
      setMemberNotice(`${member.userName} is now ${nextRole}.`);
      await loadWorkspace(false);
    } catch (reason: unknown) {
      const message = errorMessage(reason, "The member role could not be changed.");
      const stale = responseStatus(reason) === 409;
      if (stale || responseStatus(reason) === 403) {
        const refreshed = await loadWorkspace(false);
        setMemberFailure(`${message}${stale
          ? ` ${refreshed ? "Latest member data was loaded." : "Refresh and try again."}`
          : ""}`);
      } else {
        setMemberFailure(message);
      }
    } finally {
      finishOperation(key);
    }
  };

  const handleRemoveMember = async (member: WorkspaceMember) => {
    if (!window.confirm(`Remove ${member.userName} from this workspace?`)) return;
    const key = `member:${member.userId}`;
    if (!beginOperation(key)) return;
    setMemberFailure("");
    setMemberNotice("");

    try {
      await removeWorkspaceMember(parsedWorkspaceId, member.userId, member.version);
      setMemberNotice(`${member.userName} was removed from the workspace.`);
      await loadWorkspace(false);
    } catch (reason: unknown) {
      const message = errorMessage(reason,
        "This member could not be removed. They may still own or be assigned to active workspace tasks.");
      const stale = responseStatus(reason) === 409;
      if (stale || responseStatus(reason) === 403) {
        const refreshed = await loadWorkspace(false);
        setMemberFailure(`${message}${stale
          ? ` ${refreshed ? "Latest member data was loaded." : "Refresh and try again."}`
          : ""}`);
      } else {
        setMemberFailure(message);
      }
    } finally {
      finishOperation(key);
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
        ) : loading || (!failure && !hasCurrentWorkspace) ? (
          <div className="workspace-detail-state"><LoadingSpinner text="Loading workspace..." /></div>
        ) : failure || !workspace || !hasCurrentWorkspace ? (
          <div className="workspace-detail-state workspace-state--error" role="alert">
            <h2>Workspace unavailable</h2>
            <p>{failure || "This workspace could not be found."}</p>
            <button className="secondary-button" onClick={() => void loadWorkspace()} type="button">
              Try again
            </button>
          </div>
        ) : (
          <>
            <header className="workspace-detail-header">
              <div>
                <p className="eyebrow">Workspace</p>
                <h1>{workspace.name}</h1>
                <p>Created {formatDate(workspace.createdAt)}</p>
              </div>
              <div className="workspace-detail-summary">
                <div><span>Your role</span><strong>{workspace.currentUserRole}</strong></div>
                <div><span>Members</span><strong>{workspace.memberCount}</strong></div>
              </div>
            </header>

            <div className="workspace-detail-grid workspace-detail-grid--members">
              <section className="workspace-detail-card" aria-labelledby="workspace-members-title">
                <div className="workspace-section-heading">
                  <div>
                    <p className="eyebrow">People</p>
                    <h2 id="workspace-members-title">Members</h2>
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
                        <div>
                          <h3>{member.userName}</h3>
                          <p>Joined {formatDate(member.joinedAt)}</p>
                        </div>
                        <div className="workspace-member__controls">
                          <span className="workspace-role">{member.role}</span>
                          {(canChangeRole || canRemove) && (
                            <div className="workspace-row-actions">
                              {canChangeRole && (
                                <button
                                  className="workspace-text-button"
                                  disabled={memberBusy}
                                  onClick={() => void handleRoleChange(member)}
                                  type="button"
                                >
                                  {member.role === "Member" ? "Promote" : "Demote"}
                                </button>
                              )}
                              {canRemove && (
                                <button
                                  className="workspace-text-button workspace-text-button--danger"
                                  disabled={memberBusy}
                                  onClick={() => void handleRemoveMember(member)}
                                  type="button"
                                >
                                  {memberBusy ? "Working..." : "Remove"}
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                      </article>
                    );
                  })}
                </div>
              </section>

            </div>

            {canManageInvitations && (
              <section className="workspace-detail-card" aria-labelledby="workspace-management-invitations-title">
                <div className="workspace-section-heading">
                  <div>
                    <p className="eyebrow">Access</p>
                    <h2 id="workspace-management-invitations-title">Invitations</h2>
                  </div>
                  {!invitationsLoading && !invitationFailure && <span>{invitations.length} total</span>}
                </div>

                <form className="workspace-invite-form" onSubmit={handleInvite}>
                  <label htmlFor="workspace-invite-username">Invite by username</label>
                  <div>
                    <input
                      id="workspace-invite-username"
                      value={username}
                      onChange={(event) => {
                        setUsername(event.target.value);
                        if (invitationFailure) setInvitationFailure("");
                      }}
                      placeholder="Username"
                      disabled={inviting}
                      required
                    />
                    <button className="primary-button" disabled={!username.trim() || inviting} type="submit">
                      {inviting ? "Sending..." : "Send invitation"}
                    </button>
                  </div>
                  <p>New members join with the Member role.</p>
                </form>

                {invitationFailure && <p className="workspace-section-alert" role="alert">{invitationFailure}</p>}
                {invitationNotice && <p className="workspace-section-notice" role="status">{invitationNotice}</p>}

                {invitationsLoading ? (
                  <div className="workspace-compact-state"><LoadingSpinner text="Loading invitations..." /></div>
                ) : invitationFailure && invitations.length === 0 ? (
                  <div className="workspace-compact-state">
                    <button className="secondary-button" onClick={() => void loadInvitations()} type="button">
                      Try again
                    </button>
                  </div>
                ) : invitations.length === 0 ? (
                  <div className="workspace-empty-row">
                    <strong>No invitations sent</strong>
                    <span>Invite someone by username to add them to this workspace.</span>
                  </div>
                ) : (
                  <div className="workspace-invitation-list">
                    {invitations.map((invitation) => {
                      const cancelBusy = busyOperations.has(`invitation:${invitation.id}`);
                      const canCancel = isPendingAndCurrent(invitation);

                      return (
                        <article className="workspace-invitation-row" key={invitation.id}>
                          <div>
                            <div className="workspace-invitation-row__title">
                              <h3>{invitation.invitedUserName}</h3>
                              <span className={`workspace-invitation-status workspace-invitation-status--${invitation.status.toLowerCase()}`}>
                                {invitation.status}
                              </span>
                            </div>
                            <p>Invited by {invitation.invitedByUserName} · {formatDate(invitation.createdAt)}</p>
                            {invitation.expiresAt && <p>Expires {formatDate(invitation.expiresAt)}</p>}
                          </div>
                          {canCancel && (
                            <button
                              className="secondary-button"
                              disabled={cancelBusy}
                              onClick={() => void handleCancelInvitation(invitation)}
                              type="button"
                            >
                              {cancelBusy ? "Cancelling..." : "Cancel"}
                            </button>
                          )}
                        </article>
                      );
                    })}
                  </div>
                )}
              </section>
            )}

            <WorkspaceTasksSection workspace={workspace} />
          </>
        )}
      </div>
    </main>
  );
}
