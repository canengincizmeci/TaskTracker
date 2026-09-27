import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { createWorkspace, getMyWorkspaces } from "../api/workspaceService";
import { errorMessage } from "../api/errorMessage";
import LoadingSpinner from "../components/LoadingSpinner";
import type { WorkspaceListItem } from "../types/workspace";
import "../styles/workspaces.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

export default function WorkspacesPage() {
  const navigate = useNavigate();
  const [workspaces, setWorkspaces] = useState<WorkspaceListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailure, setLoadFailure] = useState("");
  const [name, setName] = useState("");
  const [creating, setCreating] = useState(false);
  const [createFailure, setCreateFailure] = useState("");
  const requestId = useRef(0);
  const createInProgress = useRef(false);
  const invalidateRequests = useCallback(() => {
    requestId.current++;
  }, []);

  const loadWorkspaces = useCallback(async () => {
    const currentRequest = ++requestId.current;
    setLoading(true);
    setLoadFailure("");

    try {
      const data = await getMyWorkspaces();
      if (currentRequest === requestId.current) setWorkspaces(data);
    } catch (reason: unknown) {
      if (currentRequest === requestId.current) {
        setLoadFailure(errorMessage(reason, "Your workspaces could not be loaded."));
      }
    } finally {
      if (currentRequest === requestId.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const request = window.setTimeout(() => {
      void loadWorkspaces();
    }, 0);

    return () => {
      window.clearTimeout(request);
      invalidateRequests();
    };
  }, [invalidateRequests, loadWorkspaces]);

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
            <p>You will be its owner and can add collaborators in a later step.</p>
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
              <p>Create your first workspace above to start organizing shared work.</p>
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
