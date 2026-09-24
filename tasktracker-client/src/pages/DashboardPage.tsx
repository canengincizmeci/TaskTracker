import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { getWorkDashboardSummary, getWorkDashboardTasks } from "../api/workDashboardService";
import { errorMessage } from "../api/errorMessage";
import DashboardSummaryCard from "../components/DashboardSummaryCard";
import DashboardWorkItem from "../components/DashboardWorkItem";
import LoadingSpinner from "../components/LoadingSpinner";
import type {
  PagedWorkTasks,
  WorkDashboardSummary,
  WorkDueFilter,
  WorkScope,
  WorkSort,
  WorkTaskPriority,
  WorkTaskQuery,
  WorkTaskStatus,
} from "../types/workDashboard";

const scopes: { value: WorkScope; label: string }[] = [
  { value: "all", label: "All" },
  { value: "owned", label: "Owned" },
  { value: "assigned", label: "Assigned" },
  { value: "shared", label: "Shared" },
];

const statuses: { value: WorkTaskStatus; label: string }[] = [
  { value: "Pending", label: "Pending" },
  { value: "InProgress", label: "In progress" },
  { value: "InReview", label: "In review" },
  { value: "Completed", label: "Completed" },
  { value: "Cancelled", label: "Cancelled" },
];

const priorities: WorkTaskPriority[] = ["Low", "Medium", "High", "Critical"];

const dueFilters: { value: WorkDueFilter; label: string }[] = [
  { value: "all", label: "All due dates" },
  { value: "overdue", label: "Overdue" },
  { value: "today", label: "Today" },
  { value: "soon", label: "Due soon" },
  { value: "none", label: "No due date" },
];

const sorts: { value: WorkSort; label: string }[] = [
  { value: "due", label: "Due date" },
  { value: "priority", label: "Priority" },
  { value: "recent", label: "Recent" },
  { value: "reviewAge", label: "Oldest review first" },
];

const validScope = new Set<WorkScope>(scopes.map(({ value }) => value));
const validStatus = new Set<WorkTaskStatus>(statuses.map(({ value }) => value));
const validPriority = new Set<WorkTaskPriority>(priorities);
const validDue = new Set<WorkDueFilter>(dueFilters.map(({ value }) => value));
const validSort = new Set<WorkSort>(sorts.map(({ value }) => value));

function valueOrDefault<T extends string>(value: string | null, valid: Set<T>, fallback: T): T {
  return value && valid.has(value as T) ? value as T : fallback;
}

function readPage(value: string | null) {
  if (!value || !/^\d+$/.test(value)) return 1;
  const page = Number(value);
  return Number.isSafeInteger(page) && page > 0 ? page : 1;
}

function countText(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

export default function DashboardPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const scope = valueOrDefault(searchParams.get("scope"), validScope, "all");
  const search = searchParams.get("search")?.trim() ?? "";
  const status = valueOrDefault<WorkTaskStatus | "">(searchParams.get("status"),
    new Set<WorkTaskStatus | "">([...validStatus, ""]), "");
  const priority = valueOrDefault<WorkTaskPriority | "">(searchParams.get("priority"),
    new Set<WorkTaskPriority | "">([...validPriority, ""]), "");
  const due = valueOrDefault(searchParams.get("due"), validDue, "all");
  const sort = valueOrDefault(searchParams.get("sort"), validSort, "due");
  const page = readPage(searchParams.get("page"));

  const [summary, setSummary] = useState<WorkDashboardSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [summaryRefreshing, setSummaryRefreshing] = useState(false);
  const [summaryFailure, setSummaryFailure] = useState("");
  const summaryRequestId = useRef(0);

  const [work, setWork] = useState<PagedWorkTasks | null>(null);
  const [workLoading, setWorkLoading] = useState(true);
  const [workFailure, setWorkFailure] = useState("");
  const workRequestId = useRef(0);
  const invalidateWorkRequests = useCallback(() => { workRequestId.current++; }, []);

  const query = useMemo<WorkTaskQuery>(() => ({
    scope,
    ...(search ? { search } : {}),
    ...(status ? { status } : {}),
    ...(priority ? { priority } : {}),
    due,
    sort,
    page,
    pageSize: 20,
  }), [due, page, priority, scope, search, sort, status]);

  const updateQuery = useCallback((changes: Partial<{
    scope: WorkScope;
    search: string;
    status: WorkTaskStatus | "";
    priority: WorkTaskPriority | "";
    due: WorkDueFilter;
    sort: WorkSort;
    page: number;
  }>, replace = false) => {
    const next = { scope, search, status, priority, due, sort, page, ...changes };
    const params = new URLSearchParams();
    if (next.scope !== "all") params.set("scope", next.scope);
    if (next.search.trim()) params.set("search", next.search.trim());
    if (next.status) params.set("status", next.status);
    if (next.priority) params.set("priority", next.priority);
    if (next.due !== "all") params.set("due", next.due);
    if (next.sort !== "due") params.set("sort", next.sort);
    if (next.page > 1) params.set("page", String(next.page));
    setSearchParams(params, { replace });
  }, [due, page, priority, scope, search, setSearchParams, sort, status]);

  const loadSummary = useCallback(async (initial = false) => {
    const currentRequest = ++summaryRequestId.current;
    if (initial) {
      setSummaryLoading(true);
      setSummaryFailure("");
    } else setSummaryRefreshing(true);
    try {
      const data = await getWorkDashboardSummary();
      if (currentRequest !== summaryRequestId.current) return;
      setSummary(data);
      setSummaryFailure("");
    } catch (reason: unknown) {
      if (currentRequest !== summaryRequestId.current) return;
      setSummaryFailure(errorMessage(reason, "Your work summary could not be loaded."));
    } finally {
      if (currentRequest === summaryRequestId.current) {
        setSummaryLoading(false);
        setSummaryRefreshing(false);
      }
    }
  }, []);

  const loadWork = useCallback(async () => {
    const currentRequest = ++workRequestId.current;
    setWorkLoading(true);
    setWorkFailure("");
    try {
      const data = await getWorkDashboardTasks(query);
      if (currentRequest !== workRequestId.current) return;
      const lastPage = Math.max(1, data.totalPages);
      if (query.page > lastPage) {
        updateQuery({ page: lastPage }, true);
        return;
      }
      setWork(data);
    } catch (reason: unknown) {
      if (currentRequest !== workRequestId.current) return;
      setWorkFailure(errorMessage(reason, "Your work list could not be loaded."));
    } finally {
      if (currentRequest === workRequestId.current) setWorkLoading(false);
    }
  }, [query, updateQuery]);

  useEffect(() => {
    const initial = window.setTimeout(() => { void loadSummary(true); }, 0);
    const refresh = () => { void loadSummary(); };
    window.addEventListener("focus", refresh);
    return () => {
      window.clearTimeout(initial);
      window.removeEventListener("focus", refresh);
    };
  }, [loadSummary]);

  useEffect(() => {
    const request = window.setTimeout(() => { void loadWork(); }, 0);
    const refresh = () => { void loadWork(); };
    window.addEventListener("focus", refresh);
    return () => {
      window.clearTimeout(request);
      window.removeEventListener("focus", refresh);
      invalidateWorkRequests();
    };
  }, [invalidateWorkRequests, loadWork]);

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    updateQuery({ search: String(data.get("search") ?? ""), page: 1 });
  };

  const allClear = summary !== null && Object.values(summary).every((count) => count === 0);
  const filtersActive = Boolean(search || status || priority || due !== "all");
  const resultStart = work && work.totalCount > 0 ? (work.page - 1) * work.pageSize + 1 : 0;
  const resultEnd = work ? Math.min(work.page * work.pageSize, work.totalCount) : 0;

  return (
    <main className="page public-page dashboard-page">
      <section className="dashboard-shell">
        <header className="dashboard-header">
          <div>
            <p className="eyebrow">ACTION CENTER</p>
            <h1>My Work</h1>
            <p>See what needs your attention across assigned work, reviews, deadlines, and invitations.</p>
          </div>
          {summary && <button type="button" className="secondary-button" disabled={summaryRefreshing}
            onClick={() => void loadSummary()}>
            {summaryRefreshing ? "Refreshing..." : "Refresh summary"}
          </button>}
        </header>

        {summaryLoading && !summary && <div className="dashboard-state" aria-live="polite">
          <LoadingSpinner text="Loading your work summary..." />
        </div>}

        {summaryFailure && !summary && <section className="dashboard-state dashboard-state--error" role="alert">
          <div><h2>We could not load your summary</h2><p>{summaryFailure}</p></div>
          <button type="button" className="primary-button" onClick={() => void loadSummary(true)}>Retry</button>
        </section>}

        {summary && <>
          {summaryFailure && <div className="dashboard-refresh-error" role="alert">
            <span>{summaryFailure}</span>
            <button type="button" onClick={() => void loadSummary()}>Retry</button>
          </div>}

          <section className="dashboard-summary-grid" aria-label="Work requiring attention">
            <DashboardSummaryCard title="Assigned to me" count={summary.assignedToMeCount}
              description={summary.assignedToMeCount === 0 ? "No active assigned tasks" :
                `${countText(summary.assignedToMeCount, "active task")} to move forward`}
              tone="assigned" to="/dashboard?scope=assigned" />
            <DashboardSummaryCard title="Awaiting my review" count={summary.awaitingMyReviewCount}
              description={summary.awaitingMyReviewCount === 0 ? "No submissions waiting" :
                `${countText(summary.awaitingMyReviewCount, "submission")} waiting for a decision`}
              tone="review" to="/tasks/awaiting-review" />
            <DashboardSummaryCard title="Overdue" count={summary.overdueCount}
              description={summary.overdueCount === 0 ? "No owned or assigned work is overdue" :
                `${countText(summary.overdueCount, "task")} past the due date`}
              tone="overdue" to="/dashboard?due=overdue" />
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

        <section className="dashboard-work" aria-labelledby="dashboard-work-heading">
          <header className="dashboard-work__header">
            <div><p className="eyebrow">WORKSPACE</p><h2 id="dashboard-work-heading">My Work</h2>
              <p>Find and open every task you own, are assigned, or can access.</p></div>
          </header>

          <div className="dashboard-scope-tabs" role="group" aria-label="Work scope">
            {scopes.map((item) => <button key={item.value} type="button"
              className={scope === item.value ? "dashboard-scope-tab dashboard-scope-tab--active" : "dashboard-scope-tab"}
              aria-pressed={scope === item.value}
              onClick={() => updateQuery({ scope: item.value, page: 1 })}>{item.label}</button>)}
          </div>

          <form className="dashboard-work-controls" onSubmit={submitSearch}>
            <div className="dashboard-search-control">
              <label htmlFor="dashboard-search">Search work</label>
              <div><input key={search} id="dashboard-search" name="search" type="search"
                defaultValue={search} placeholder="Title, description, or category" />
                <button type="submit" className="secondary-button">Search</button>
                {search && <button type="button" className="dashboard-text-button"
                  onClick={() => updateQuery({ search: "", page: 1 })}>Clear search</button>}
              </div>
            </div>

            <div className="dashboard-filter-grid">
              <label>Status<select value={status}
                onChange={(event) => updateQuery({ status: event.target.value as WorkTaskStatus | "", page: 1 })}>
                <option value="">All statuses</option>
                {statuses.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select></label>
              <label>Priority<select value={priority}
                onChange={(event) => updateQuery({ priority: event.target.value as WorkTaskPriority | "", page: 1 })}>
                <option value="">All priorities</option>
                {priorities.map((item) => <option key={item} value={item}>{item}</option>)}
              </select></label>
              <label>Due<select value={due}
                onChange={(event) => updateQuery({ due: event.target.value as WorkDueFilter, page: 1 })}>
                {dueFilters.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select></label>
              <label>Sort<select value={sort}
                onChange={(event) => updateQuery({ sort: event.target.value as WorkSort, page: 1 })}>
                {sorts.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select></label>
            </div>

            {filtersActive && <button type="button" className="dashboard-clear-filters"
              onClick={() => updateQuery({ search: "", status: "", priority: "", due: "all", page: 1 })}>
              Clear search and filters
            </button>}
          </form>

          <div className="dashboard-work__results" aria-live="polite">
            <span>{work ? work.totalCount === 0 ? "No tasks" :
              `Showing ${resultStart}–${resultEnd} of ${work.totalCount}` : "Loading results"}</span>
            {workLoading && work && <span className="dashboard-work__updating">Updating results…</span>}
          </div>

          {workLoading && !work && <div className="dashboard-work-state">
            <LoadingSpinner text="Loading your work..." />
          </div>}

          {workFailure && !work && <section className="dashboard-work-state dashboard-work-state--error" role="alert">
            <h3>We could not load your work</h3><p>{workFailure}</p>
            <button type="button" className="primary-button" onClick={() => void loadWork()}>Retry</button>
          </section>}

          {workFailure && work && <div className="dashboard-refresh-error" role="alert">
            <span>{workFailure} Previous results are still shown.</span>
            <button type="button" onClick={() => void loadWork()}>Retry</button>
          </div>}

          {work && work.items.length > 0 && <div className="dashboard-work-list" aria-busy={workLoading}>
            {work.items.map((task) => <DashboardWorkItem key={task.taskId} task={task} />)}
          </div>}

          {work && !workLoading && work.items.length === 0 && <section className="dashboard-work-empty">
            <h3>{search ? "No tasks match your search" : scope === "assigned" && !filtersActive ?
              "No active tasks are currently assigned to you" : "No work matches this view"}</h3>
            <p>{filtersActive ? "Try clearing your search or filters to see more work." :
              "Tasks will appear here when they become relevant to you."}</p>
            <div>
              {filtersActive && <button type="button" className="secondary-button"
                onClick={() => updateQuery({ search: "", status: "", priority: "", due: "all", page: 1 })}>
                Clear search and filters
              </button>}
              {scope === "all" && !filtersActive && <Link className="primary-button" to="/tasks/create-task">Create Task</Link>}
            </div>
          </section>}

          {work && work.totalPages > 1 && <nav className="dashboard-pagination" aria-label="Work list pages">
            <button type="button" className="secondary-button" disabled={workLoading || page <= 1}
              onClick={() => updateQuery({ page: page - 1 })}>Previous</button>
            <span>Page <strong>{work.page}</strong> of <strong>{work.totalPages}</strong></span>
            <button type="button" className="secondary-button" disabled={workLoading || page >= work.totalPages}
              onClick={() => updateQuery({ page: page + 1 })}>Next</button>
          </nav>}
        </section>
      </section>
    </main>
  );
}
