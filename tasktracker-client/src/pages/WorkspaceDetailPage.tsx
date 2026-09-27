import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getWorkspace } from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "../components/LoadingSpinner";
import type { WorkspaceDetail } from "../types/workspace";
import "../styles/workspaces.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

export default function WorkspaceDetailPage() {
  const { workspaceId } = useParams();
  const parsedWorkspaceId = Number(workspaceId);
  const validWorkspaceId = Number.isSafeInteger(parsedWorkspaceId) && parsedWorkspaceId > 0;
  const [workspace, setWorkspace] = useState<WorkspaceDetail | null>(null);
  const [loading, setLoading] = useState(validWorkspaceId);
  const [failure, setFailure] = useState(validWorkspaceId ? "" : "This workspace address is invalid.");
  const requestId = useRef(0);
  const invalidateRequests = useCallback(() => {
    requestId.current++;
  }, []);

  const loadWorkspace = useCallback(async () => {
    if (!validWorkspaceId) return;

    const currentRequest = ++requestId.current;
    setLoading(true);
    setFailure("");

    try {
      const data = await getWorkspace(parsedWorkspaceId);
      if (currentRequest === requestId.current) setWorkspace(data);
    } catch (reason: unknown) {
      if (currentRequest === requestId.current) {
        setFailure(errorMessage(reason, "This workspace could not be loaded."));
      }
    } finally {
      if (currentRequest === requestId.current) setLoading(false);
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

            <div className="workspace-detail-grid">
              <section className="workspace-detail-card" aria-labelledby="workspace-members-title">
                <div className="workspace-section-heading">
                  <div>
                    <p className="eyebrow">People</p>
                    <h2 id="workspace-members-title">Members</h2>
                  </div>
                  <span>{workspace.memberCount} total</span>
                </div>

                <div className="workspace-member-list">
                  {workspace.members.map((member) => (
                    <article className="workspace-member" key={member.userId}>
                      <div className="workspace-member__avatar" aria-hidden="true">
                        {member.userName.trim().charAt(0).toUpperCase() || "?"}
                      </div>
                      <div>
                        <h3>{member.userName}</h3>
                        <p>Joined {formatDate(member.joinedAt)}</p>
                      </div>
                      <span className="workspace-role">{member.role}</span>
                    </article>
                  ))}
                </div>
              </section>

              <section className="workspace-detail-card workspace-tasks-placeholder" aria-labelledby="workspace-tasks-title">
                <div className="workspace-state__icon" aria-hidden="true">✓</div>
                <p className="eyebrow">Tasks</p>
                <h2 id="workspace-tasks-title">Task management coming next</h2>
                <p>Workspace tasks will appear here in a future update.</p>
              </section>
            </div>
          </>
        )}
      </div>
    </main>
  );
}
