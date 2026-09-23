import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { getWorkDashboardSummary } from "../api/workDashboardService";
import { errorMessage } from "../api/errorMessage";
import DashboardSummaryCard from "../components/DashboardSummaryCard";
import LoadingSpinner from "../components/LoadingSpinner";
import type { WorkDashboardSummary } from "../types/workDashboard";

function countText(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

export default function DashboardPage() {
  const [summary, setSummary] = useState<WorkDashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [failure, setFailure] = useState("");
  const requestId = useRef(0);

  const loadSummary = useCallback(async (initial = false) => {
    const currentRequest = ++requestId.current;
    if (initial) {
      setLoading(true);
      setFailure("");
    }
    else setRefreshing(true);
    try {
      const data = await getWorkDashboardSummary();
      if (currentRequest !== requestId.current) return;
      setSummary(data);
      setFailure("");
    } catch (reason: unknown) {
      if (currentRequest !== requestId.current) return;
      setFailure(errorMessage(reason, "Your work summary could not be loaded."));
    } finally {
      if (currentRequest === requestId.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    const initial = window.setTimeout(() => { void loadSummary(true); }, 0);
    const refresh = () => { void loadSummary(); };
    window.addEventListener("focus", refresh);
    return () => {
      window.clearTimeout(initial);
      window.removeEventListener("focus", refresh);
    };
  }, [loadSummary]);

  const allClear = summary !== null && Object.values(summary).every((count) => count === 0);

  return (
    <main className="page public-page dashboard-page">
      <section className="dashboard-shell">
        <header className="dashboard-header">
          <div>
            <p className="eyebrow">ACTION CENTER</p>
            <h1>My Work</h1>
            <p>See what needs your attention across assigned work, reviews, deadlines, and invitations.</p>
          </div>
          {summary && <button type="button" className="secondary-button" disabled={refreshing}
            onClick={() => void loadSummary()}>
            {refreshing ? "Refreshing..." : "Refresh"}
          </button>}
        </header>

        {loading && !summary && <div className="dashboard-state" aria-live="polite">
          <LoadingSpinner text="Loading your work summary..." />
        </div>}

        {failure && !summary && <section className="dashboard-state dashboard-state--error" role="alert">
          <div><h2>We could not load My Work</h2><p>{failure}</p></div>
          <button type="button" className="primary-button"
            onClick={() => void loadSummary(true)}>
            Retry
          </button>
        </section>}

        {summary && <>
          {failure && <div className="dashboard-refresh-error" role="alert">
            <span>{failure}</span>
            <button type="button" onClick={() => void loadSummary()}>Retry</button>
          </div>}

          <section className="dashboard-summary-grid" aria-label="Work requiring attention">
            <DashboardSummaryCard title="Assigned to me" count={summary.assignedToMeCount}
              description={summary.assignedToMeCount === 0 ? "No active assigned tasks" :
                `${countText(summary.assignedToMeCount, "active task")} to move forward`}
              tone="assigned" to="/tasks/assigned-to-me" />
            <DashboardSummaryCard title="Awaiting my review" count={summary.awaitingMyReviewCount}
              description={summary.awaitingMyReviewCount === 0 ? "No submissions waiting" :
                `${countText(summary.awaitingMyReviewCount, "submission")} waiting for a decision`}
              tone="review" to="/tasks/awaiting-review" />
            <DashboardSummaryCard title="Overdue" count={summary.overdueCount}
              description={summary.overdueCount === 0 ? "No owned or assigned work is overdue" :
                `${countText(summary.overdueCount, "task")} past the due date`}
              tone="overdue" />
            <DashboardSummaryCard title="Pending invitations" count={summary.pendingInvitationCount}
              description={summary.pendingInvitationCount === 0 ? "No invitations need a response" :
                `${countText(summary.pendingInvitationCount, "invitation")} awaiting your response`}
              tone="invitation" to="/tasks/invitations" />
          </section>

          {allClear && <section className="dashboard-zero-state">
            <div className="dashboard-zero-state__icon" aria-hidden="true">✓</div>
            <div><h2>Nothing needs your attention right now</h2>
              <p>Create a task when you are ready, or browse the work you already own.</p></div>
            <div className="dashboard-zero-state__actions">
              <Link className="primary-button" to="/tasks/create-task">Create Task</Link>
              <Link className="secondary-button" to="/tasks/user-tasks">Owned Tasks</Link>
            </div>
          </section>}
        </>}
      </section>
    </main>
  );
}
