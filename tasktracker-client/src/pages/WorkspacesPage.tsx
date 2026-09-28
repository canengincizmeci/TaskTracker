import { isAxiosError } from "axios";
import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  acceptWorkspaceInvitation,
  createWorkspace,
  getMyWorkspaceInvitations,
  getMyWorkspaces,
  rejectWorkspaceInvitation,
} from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "../components/LoadingSpinner";
import type { WorkspaceInvitation, WorkspaceListItem } from "../types/workspace";
import "../styles/workspaces.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function isConflict(error: unknown) {
  return isAxiosError(error) && error.response?.status === 409;
}

function isPendingAndCurrent(invitation: WorkspaceInvitation) {
  return invitation.status === "Pending" &&
    (!invitation.expiresAt || new Date(invitation.expiresAt).getTime() > Date.now());
}

export default function WorkspacesPage() {
  const navigate = useNavigate();
  const [workspaces, setWorkspaces] = useState<WorkspaceListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailure, setLoadFailure] = useState("");
  const [name, setName] = useState("");
  const [creating, setCreating] = useState(false);
  const [createFailure, setCreateFailure] = useState("");
  const workspaceRequestId = useRef(0);
  const createInProgress = useRef(false);

  const [invitations, setInvitations] = useState<WorkspaceInvitation[]>([]);
  const [invitationsLoading, setInvitationsLoading] = useState(true);
  const [invitationFailure, setInvitationFailure] = useState("");
  const [invitationNotice, setInvitationNotice] = useState("");
  const invitationRequestId = useRef(0);
  const activeInvitationRequests = useRef(new Set<number>());
  const [busyInvitationIds, setBusyInvitationIds] = useState<Set<number>>(new Set());

  const invalidateRequests = useCallback(() => {
    workspaceRequestId.current++;
    invitationRequestId.current++;
  }, []);

  const loadWorkspaces = useCallback(async (initial = true) => {
    const currentRequest = ++workspaceRequestId.current;
    if (initial) setLoading(true);
    setLoadFailure("");

    try {
      const data = await getMyWorkspaces();
      if (currentRequest === workspaceRequestId.current) setWorkspaces(data);
      return true;
    } catch (reason: unknown) {
      if (currentRequest === workspaceRequestId.current) {
        setLoadFailure(errorMessage(reason, "Your workspaces could not be loaded."));
      }
      return false;
    } finally {
      if (initial && currentRequest === workspaceRequestId.current) setLoading(false);
    }
  }, []);

  const loadInvitations = useCallback(async (initial = true) => {
    const currentRequest = ++invitationRequestId.current;
    if (initial) setInvitationsLoading(true);
    setInvitationFailure("");

    try {
      const data = await getMyWorkspaceInvitations();
      if (currentRequest === invitationRequestId.current) setInvitations(data);
      return true;
    } catch (reason: unknown) {
      if (currentRequest === invitationRequestId.current) {
        setInvitationFailure(errorMessage(reason, "Your workspace invitations could not be loaded."));
      }
      return false;
    } finally {
      if (initial && currentRequest === invitationRequestId.current) setInvitationsLoading(false);
    }
  }, []);

  useEffect(() => {
    const request = window.setTimeout(() => {
      void Promise.all([loadWorkspaces(), loadInvitations()]);
    }, 0);

    return () => {
      window.clearTimeout(request);
      invalidateRequests();
    };
  }, [invalidateRequests, loadInvitations, loadWorkspaces]);

  const handleCreate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedName = name.trim();

    if (!normalizedName || createInProgress.current) return;

    createInProgress.current = true;
    setCreating(true);
    setCreateFailure("");

    try {
      const workspace = await createWorkspace(normalizedName);
      navigate(`/workspaces/${workspace.id}`);
    } catch (reason: unknown) {
      setCreateFailure(errorMessage(reason, "The workspace could not be created."));
      createInProgress.current = false;
      setCreating(false);
    }
  };

  const respondToInvitation = async (invitation: WorkspaceInvitation, accept: boolean) => {
    if (activeInvitationRequests.current.has(invitation.id)) return;

    activeInvitationRequests.current.add(invitation.id);
    setBusyInvitationIds(new Set(activeInvitationRequests.current));
    setInvitationFailure("");
    setInvitationNotice("");

    try {
      await (accept
        ? acceptWorkspaceInvitation(invitation.id, invitation.version)
        : rejectWorkspaceInvitation(invitation.id, invitation.version));
      setInvitations((current) => current.filter((item) => item.id !== invitation.id));
      setInvitationNotice(accept
        ? `You joined ${invitation.workspaceName}.`
        : `Invitation to ${invitation.workspaceName} rejected.`);
      if (accept) await loadWorkspaces(false);
    } catch (reason: unknown) {
      const message = errorMessage(reason, "The workspace invitation could not be updated.");
      if (isConflict(reason)) {
        const refreshed = await loadInvitations(false);
        setInvitationFailure(`${message} ${refreshed ? "Latest invitations were loaded." : "Refresh and try again."}`);
      } else {
        setInvitationFailure(message);
      }
    } finally {
      activeInvitationRequests.current.delete(invitation.id);
      setBusyInvitationIds(new Set(activeInvitationRequests.current));
    }
  };

  return (
    <main className="page workspace-page">
      <div className="workspace-shell">
        <header className="workspace-header">
          <div>
            <p className="eyebrow">Shared work</p>
            <h1>Workspaces</h1>
            <p>Create focused spaces for a group and see the spaces you belong to.</p>
          </div>
        </header>

        <section className="workspace-create-card" aria-labelledby="create-workspace-title">
          <div>
            <p className="eyebrow">New workspace</p>
            <h2 id="create-workspace-title">Start a workspace</h2>
            <p>You will be its owner and can invite collaborators from its detail page.</p>
          </div>

          <form className="workspace-create-form" onSubmit={handleCreate}>
            <label htmlFor="workspace-name">Workspace name</label>
            <div>
              <input
                id="workspace-name"
                value={name}
                maxLength={150}
                onChange={(event) => {
                  setName(event.target.value);
                  if (createFailure) setCreateFailure("");
                }}
                placeholder="Example: Product launch"
                disabled={creating}
                required
              />
              <button className="primary-button" disabled={!name.trim() || creating} type="submit">
                {creating ? "Creating..." : "Create workspace"}
              </button>
            </div>
            {createFailure && <p className="workspace-inline-error" role="alert">{createFailure}</p>}
          </form>
        </section>

        <section className="workspace-list-card" aria-labelledby="workspace-invitations-title">
          <div className="workspace-section-heading">
            <div>
              <p className="eyebrow">Invitations</p>
              <h2 id="workspace-invitations-title">Workspace invitations</h2>
            </div>
            {!invitationsLoading && !invitationFailure && <span>{invitations.length} pending</span>}
          </div>

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
              <strong>No pending invitations</strong>
              <span>Workspace invitations sent to you will appear here.</span>
            </div>
          ) : (
            <div className="workspace-invitation-list">
              {invitations.map((invitation) => {
                const busy = busyInvitationIds.has(invitation.id);
                const canRespond = isPendingAndCurrent(invitation);
                return (
                  <article className="workspace-invitation-row" key={invitation.id}>
                    <div>
                      <div className="workspace-invitation-row__title">
                        <h3>{invitation.workspaceName}</h3>
                        <span className={`workspace-invitation-status workspace-invitation-status--${invitation.status.toLowerCase()}`}>
                          {invitation.status}
                        </span>
                      </div>
                      <p>Invited by {invitation.invitedByUserName} · {formatDate(invitation.createdAt)}</p>
                      {invitation.expiresAt && <p>Expires {formatDate(invitation.expiresAt)}</p>}
                    </div>
                    {canRespond && (
                      <div className="workspace-row-actions">
                        <button
                          className="primary-button"
                          disabled={busy}
                          onClick={() => void respondToInvitation(invitation, true)}
                          type="button"
                        >
                          {busy ? "Working..." : "Accept"}
                        </button>
                        <button
                          className="secondary-button"
                          disabled={busy}
                          onClick={() => void respondToInvitation(invitation, false)}
                          type="button"
                        >
                          Reject
                        </button>
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
          )}
        </section>

        <section className="workspace-list-card" aria-labelledby="your-workspaces-title">
          <div className="workspace-section-heading">
            <div>
              <p className="eyebrow">Your spaces</p>
              <h2 id="your-workspaces-title">Your workspaces</h2>
            </div>
            {!loading && !loadFailure && <span>{workspaces.length} total</span>}
          </div>

          {loading ? (
            <div className="workspace-state"><LoadingSpinner text="Loading workspaces..." /></div>
          ) : loadFailure ? (
            <div className="workspace-state workspace-state--error" role="alert">
              <h3>We could not load your workspaces</h3>
              <p>{loadFailure}</p>
              <button className="secondary-button" onClick={() => void loadWorkspaces()} type="button">
                Try again
              </button>
            </div>
          ) : workspaces.length === 0 ? (
            <div className="workspace-state">
              <div className="workspace-state__icon" aria-hidden="true">W</div>
              <h3>No workspaces yet</h3>
              <p>Create your first workspace above or accept an invitation to join one.</p>
            </div>
          ) : (
            <div className="workspace-grid">
              {workspaces.map((workspace) => (
                <Link className="workspace-card" key={workspace.id} to={`/workspaces/${workspace.id}`}>
                  <div className="workspace-card__top">
                    <span className="workspace-role">{workspace.role}</span>
                    <span aria-hidden="true">→</span>
                  </div>
                  <h3>{workspace.name}</h3>
                  <p>Created {formatDate(workspace.createdAt)}</p>
                </Link>
              ))}
            </div>
          )}
        </section>
      </div>
    </main>
  );
}
