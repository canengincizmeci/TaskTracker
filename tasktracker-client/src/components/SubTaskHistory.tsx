import { taskStatusLabel } from "../utils/taskDisplay";
import type { SubTaskActivity, SubTaskSubmission } from "../types/subTask";
import type { TaskAttachment } from "../types/taskAttachment";
import { AttachmentChips } from "./TaskFileControls";

const activityLabels: Record<string, string> = {
  TaskCreated: "created the subtask",
  TaskDetailsUpdated: "edited the subtask",
  UserAssigned: "assigned the subtask",
  UserUnassigned: "removed the assignee",
  WorkStarted: "started work",
  SubmissionCreated: "submitted work",
  ChangesRequested: "requested changes",
  SubmissionApproved: "approved the submission",
  TaskCompleted: "completed the subtask",
  TaskCancelled: "cancelled the subtask",
  TaskReopened: "reopened the subtask",
  ResponsibilityReset: "reset work after an assignment change",
};

function When({ value }: { value: string }) {
  return <time dateTime={value}>{new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}</time>;
}

export function SubTaskRevision({ item, files, download }: {
  item: SubTaskSubmission; files: TaskAttachment[]; download: (file: TaskAttachment) => Promise<void>;
}) {
  return <div className="wf-revision-body">
    <p className="st-muted">{item.submittedByUserName || "Assignee"} · <When value={item.createdAt} /></p>
    <p className="wf-summary">{item.content}</p>
    <AttachmentChips files={files.filter(file => file.subTaskSubmissionId === item.id)} download={download} />
    {item.review && <div className={`wf-verdict${item.review.decision === "Approved" ? " wf-verdict--approved" : ""}`}>
      <strong>{item.review.decision === "Approved" ? "Approved" : "Changes requested"}</strong>
      <span>{item.review.reviewerUserName || "Reviewer"} · <When value={item.review.createdAt} /></span>
      {item.review.feedback && <p>{item.review.feedback}</p>}
    </div>}
  </div>;
}

export default function SubTaskHistory({ submissions, activities, files, download }: {
  submissions: SubTaskSubmission[]; activities: SubTaskActivity[]; files: TaskAttachment[];
  download: (file: TaskAttachment) => Promise<void>;
}) {
  const revisions = new Map(submissions.map(item => [item.id, item.revisionNumber]));
  return <div className="st-history">
    <details>
      <summary>Submission history <span>({submissions.length})</span></summary>
      {submissions.length === 0 ? <p className="st-muted">No work has been submitted.</p> : submissions.map(item =>
        <details className="wf-history-entry" key={item.id}>
          <summary><strong>Revision {item.revisionNumber}</strong><span>{item.review?.decision === "Approved" ?
            "Approved" : item.review ? "Changes requested" : "Submitted"}</span></summary>
          <SubTaskRevision item={item} files={files} download={download} />
        </details>)}
    </details>
    <details>
      <summary>Subtask activity</summary>
      {activities.length === 0 ? <p className="st-muted">No activity has been recorded.</p> :
        <ol className="st-events">{activities.slice().reverse().map(item => {
          const revision = item.submissionId == null ? undefined : revisions.get(item.submissionId);
          return <li key={item.id}>
            <div><strong>{item.actorUserName || "A participant"}</strong> {activityLabels[item.activityType] || "updated the subtask"}
              {item.targetUserName && ` (${item.targetUserName})`}{revision != null && ` · Revision ${revision}`}
              {item.fromStatus && item.toStatus && <small>{taskStatusLabel(item.fromStatus)} → {taskStatusLabel(item.toStatus)}</small>}</div>
            <When value={item.createdAt} />
          </li>;
        })}</ol>}
    </details>
  </div>;
}
